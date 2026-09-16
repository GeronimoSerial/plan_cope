using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class NodeCredentialServiceTests
{
    private const string Cue = "180000100";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task IssueForNodeAsync_ReturnsPlaintextRefreshTokenAndPersistsOnlyItsHash()
    {
        using var dbContext = CreateDbContext();
        var key = CreateKey();
        var node = CreateNode(key.Id);
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(node);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var issued = await service.IssueForNodeAsync(node, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.NotNull(issued.PlaintextRefreshToken);
        Assert.NotEmpty(issued.AccessToken);

        var stored = await dbContext.NodeCredentials.SingleAsync();
        Assert.Equal(node.Id, stored.NodeId);
        Assert.Equal(issued.Credential.Id, stored.Id);
        Assert.NotEqual(issued.PlaintextRefreshToken, stored.RefreshTokenHash);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issued.PlaintextRefreshToken))).ToLowerInvariant(),
            stored.RefreshTokenHash);
    }

    [Fact]
    public async Task FindOrEnrollAsync_ReusesExistingNodeWithoutIncrementingCount()
    {
        using var dbContext = CreateDbContext();
        var key = CreateKey();
        var existing = CreateNode(key.Id, "fp-existing");
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var (node, isNewNode) = await service.FindOrEnrollAsync(
            key, Cue, "fp-existing", JsonDocument.Parse("{}"), "1.0.0", CancellationToken.None);

        Assert.False(isNewNode);
        Assert.Equal(existing.Id, node.Id);

        var reloaded = await dbContext.ActivationKeys.SingleAsync();
        Assert.Equal(0, reloaded.ActivationCount);
    }

    [Fact]
    public async Task FindOrEnrollAsync_NewFingerprintIncrementsCountAndCreatesNode()
    {
        using var dbContext = CreateDbContext();
        var key = CreateKey();
        dbContext.ActivationKeys.Add(key);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var (node, isNewNode) = await service.FindOrEnrollAsync(
            key, Cue, "fp-new", JsonDocument.Parse("{}"), "1.0.0", CancellationToken.None);

        Assert.True(isNewNode);
        Assert.Equal(key.Id, node.ActivationKeyId);
        Assert.Equal("Active", node.Status);

        var reloaded = await dbContext.ActivationKeys.SingleAsync();
        Assert.Equal(1, reloaded.ActivationCount);
    }

    private static ActivationKey CreateKey()
    {
        var now = DateTimeOffset.UtcNow;
        return new ActivationKey(
            NewId(),
            "argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$aGFzaA==",
            "PCOPE-01",
            "test-issuer",
            now,
            ExpiresAt: null,
            MaxActivations: 5,
            ActivationCount: 0,
            RevokedAt: null,
            RevokedReason: null,
            ScopeCue: null,
            Note: "Clave de prueba");
    }

    private static RegisteredNode CreateNode(string activationKeyId, string fingerprintHash = "fp-1")
    {
        var now = DateTimeOffset.UtcNow;
        return new RegisteredNode(
            NewId(),
            SchoolId: null,
            Guid.NewGuid().ToString("N"),
            DeviceName: null,
            Status: "Active",
            LastSeenAt: null,
            now,
            now,
            fingerprintHash,
            JsonDocument.Parse("""{"mac":"00:11:22:33:44:55"}"""),
            Cue,
            activationKeyId,
            now,
            RevokedAt: null,
            AppVersion: "1.0.0");
    }

    private static NodeCredentialService CreateService(PlanCopeDbContext dbContext)
    {
        var options = Options.Create(new AuthOptions
        {
            SigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        return new NodeCredentialService(dbContext, new TokenService(options));
    }

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
        return new PlanCopeDbContext(options);
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
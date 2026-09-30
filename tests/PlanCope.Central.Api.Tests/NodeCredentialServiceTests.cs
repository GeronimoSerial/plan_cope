using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
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
        var key = CreateKey(scopeCue: Cue);
        var existing = CreateNode(key.Id, "fp-existing");
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var (node, isNewNode) = await service.FindOrEnrollAsync(
            key, "fp-existing", JsonDocument.Parse("{}"), "1.0.0", CancellationToken.None);

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
            key, "fp-new", JsonDocument.Parse("{}"), "1.0.0", CancellationToken.None);

        Assert.True(isNewNode);
        Assert.Equal(key.Id, node.ActivationKeyId);
        Assert.Equal("Active", node.Status);
        Assert.Equal(string.Empty, node.Cue);

        var reloaded = await dbContext.ActivationKeys.SingleAsync();
        Assert.Equal(1, reloaded.ActivationCount);
    }

    [Fact]
    public async Task FindOrEnrollAsync_DoesNotUpgrade_existing_CUE_bound_node_for_universal_key()
    {
        using var dbContext = CreateDbContext();
        var key = CreateKey();
        var existing = CreateNode(key.Id, "fp-existing");
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var (node, isNewNode) = await service.FindOrEnrollAsync(
            key, "fp-existing", JsonDocument.Parse("{}"), "1.0.0", CancellationToken.None);

        Assert.False(isNewNode);
        Assert.Equal(Cue, node.Cue);
        Assert.Equal(Cue, (await dbContext.RegisteredNodes.SingleAsync()).Cue);
    }

    [Fact]
    public async Task Refresh_token_allows_node_to_revalidate_after_twenty_days_offline()
    {
        using var dbContext = CreateDbContext();
        var key = CreateKey();
        var node = CreateNode(key.Id, "fp-lifetime");
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(node);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, revalidationDays: 30, configuredRefreshDays: 7);
        var issued = await service.IssueForNodeAsync(node, CancellationToken.None);
        var simulatedNow = DateTimeOffset.UtcNow;
        dbContext.Entry(issued.Credential).CurrentValues.SetValues(issued.Credential with
        {
            IssuedAt = simulatedNow.AddDays(-20),
            ExpiresAt = simulatedNow.AddDays(10)
        });
        await dbContext.SaveChangesAsync();

        Assert.True(issued.RefreshTokenExpiresAt >= simulatedNow.AddDays(16));
        Assert.NotNull(await service.RotateAsync(issued.PlaintextRefreshToken, CancellationToken.None));
    }

    private static ActivationKey CreateKey(string? scopeCue = null)
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
            ScopeCue: scopeCue,
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

    private static NodeCredentialService CreateService(PlanCopeDbContext dbContext, int revalidationDays = 30, int configuredRefreshDays = 7)
    {
        var options = Options.Create(new AuthOptions
        {
            SigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Activation:RevalidationIntervalDays"] = revalidationDays.ToString(),
            ["Auth:RefreshTokenDays"] = configuredRefreshDays.ToString()
        }).Build();
        return new NodeCredentialService(dbContext, new TokenService(options), configuration);
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

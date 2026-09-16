using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ReleaseGateServiceTests
{
    private const string Cue = "180000100";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task ResolveAsync_NodeOutsideTheRing_GetsNothing()
    {
        using var dbContext = CreateDbContext();
        var node = CreateNode();
        dbContext.RegisteredNodes.Add(node);
        dbContext.Set<ReleaseRing>().Add(CreateRing(channel: "beta", rolloutMode: "AllEnrolled", rolloutPercentage: null, version: "2.0.0"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var decision = await service.ResolveAsync(node.Id, "1.0.0", "stable", CancellationToken.None);

        Assert.False(decision.MayInstall);
        Assert.Null(decision.TargetVersion);
        Assert.Null(decision.DownloadUrl);
        Assert.Null(decision.Sha256);
    }

    [Fact]
    public async Task ResolveAsync_AllEnrolled_GetsTheVersion()
    {
        using var dbContext = CreateDbContext();
        var node = CreateNode();
        dbContext.RegisteredNodes.Add(node);
        dbContext.Set<ReleaseRing>().Add(CreateRing(channel: "stable", rolloutMode: "AllEnrolled", rolloutPercentage: null, version: "2.0.0"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var decision = await service.ResolveAsync(node.Id, "1.0.0", "stable", CancellationToken.None);

        Assert.True(decision.MayInstall);
        Assert.Equal("2.0.0", decision.TargetVersion);
        Assert.Equal("https://downloads.example.test/stable/2.0.0", decision.DownloadUrl);
        Assert.Equal(Sha256Of("2.0.0"), decision.Sha256);
    }

    [Fact]
    public async Task ResolveAsync_PercentageOfEnrolled_IsDeterministicAcrossRepeatedCalls()
    {
        using var dbContext = CreateDbContext();
        var node = CreateNode();
        dbContext.RegisteredNodes.Add(node);
        dbContext.Set<ReleaseRing>().Add(CreateRing(channel: "stable", rolloutMode: "PercentageOfEnrolled", rolloutPercentage: 50, version: "2.0.0"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var first = await service.ResolveAsync(node.Id, "1.0.0", "stable", CancellationToken.None);

        for (var i = 0; i < 10; i++)
        {
            var next = await service.ResolveAsync(node.Id, "1.0.0", "stable", CancellationToken.None);
            Assert.Equal(first, next);
        }
    }

    [Fact]
    public async Task ResolveAsync_NodeAlreadyOnNewestVersion_GetsNothing()
    {
        using var dbContext = CreateDbContext();
        var node = CreateNode(appVersion: "2.0.0");
        dbContext.RegisteredNodes.Add(node);
        dbContext.Set<ReleaseRing>().Add(CreateRing(channel: "stable", rolloutMode: "AllEnrolled", rolloutPercentage: null, version: "2.0.0"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var decision = await service.ResolveAsync(node.Id, "2.0.0", "stable", CancellationToken.None);

        Assert.False(decision.MayInstall);
        Assert.Null(decision.TargetVersion);
        Assert.Null(decision.DownloadUrl);
        Assert.Null(decision.Sha256);
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredNodeId_GetsNothing()
    {
        using var dbContext = CreateDbContext();
        dbContext.Set<ReleaseRing>().Add(CreateRing(channel: "stable", rolloutMode: "AllEnrolled", rolloutPercentage: null, version: "2.0.0"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var decision = await service.ResolveAsync("node-not-registered", "1.0.0", "stable", CancellationToken.None);

        Assert.False(decision.MayInstall);
        Assert.Null(decision.TargetVersion);
        Assert.Null(decision.DownloadUrl);
        Assert.Null(decision.Sha256);
    }

    private static RegisteredNode CreateNode(string appVersion = "1.0.0")
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
            "fp-1",
            JsonDocument.Parse("""{"mac":"00:11:22:33:44:55"}"""),
            Cue,
            ActivationKeyId: null,
            now,
            RevokedAt: null,
            AppVersion: appVersion);
    }

    private static ReleaseRing CreateRing(string channel, string rolloutMode, int? rolloutPercentage, string version)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReleaseRing(
            Guid.NewGuid(),
            version,
            channel,
            Sha256Of(version),
            $"https://downloads.example.test/{channel}/{version}",
            rolloutMode,
            rolloutPercentage,
            now,
            Guid.NewGuid());
    }

    private static ReleaseGateService CreateService(PlanCopeDbContext dbContext)
    {
        return new ReleaseGateService(dbContext);
    }

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
        return new PlanCopeDbContext(options);
    }

    private static string Sha256Of(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
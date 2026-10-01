using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the sync-pull cursor contract: an opaque UtcTicks cursor is compared as a DateTimeOffset so
/// the predicate translates on PostgreSQL, and a non-numeric, negative, or out-of-range cursor is
/// clamped to 0 instead of throwing.
/// </summary>
public sealed class SyncPullCursorTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Non_numeric_cursor_is_treated_as_zero()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1);
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, "abc");

        Assert.Equal("pkg-1", Assert.Single(response.Items).EntityId);
    }

    [Fact]
    public async Task Negative_cursor_is_treated_as_zero()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1);
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, "-5");

        Assert.Equal("pkg-1", Assert.Single(response.Items).EntityId);
    }

    [Fact]
    public async Task Cursor_larger_than_datetime_max_is_treated_as_zero()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1);
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, long.MaxValue.ToString());

        Assert.Equal("pkg-1", Assert.Single(response.Items).EntityId);
    }

    [Fact]
    public async Task Cursor_at_datetime_max_returns_no_items()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1);
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, DateTimeOffset.MaxValue.UtcTicks.ToString());

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Valid_cursor_returns_only_packages_published_after_it()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1);
        await SeedPackageAsync(dbContext, "pkg-2", 2);
        var controller = CreateController(dbContext);

        var first = await PullAsync(controller, "0", limit: 1);
        Assert.Equal("pkg-1", Assert.Single(first.Items).EntityId);

        var second = await PullAsync(controller, first.NextCursor);

        Assert.Equal("pkg-2", Assert.Single(second.Items).EntityId);
        Assert.Equal(BaseTime.AddMinutes(2).UtcTicks.ToString(), second.NextCursor);
    }

    private static async Task SeedPackageAsync(PlanCopeDbContext dbContext, string packageId, int publishedMinuteOffset)
    {
        var publishedAt = BaseTime.AddMinutes(publishedMinuteOffset);
        var examId = $"ex-{packageId}";
        var versionId = $"ev-{packageId}";

        dbContext.Exams.Add(new Exam(
            examId,
            $"CODE-{packageId}",
            "Exam",
            Description: null,
            Level: null,
            Area: null,
            Subject: "Matematica",
            Status: "Published",
            DeletedAt: null,
            CreatedAt: publishedAt,
            UpdatedAt: publishedAt));
        dbContext.ExamVersions.Add(new ExamVersion(
            versionId,
            examId,
            1,
            1,
            "Published",
            Metadata: null,
            CreatedBy: null,
            ReviewedBy: null,
            ApprovedBy: null,
            PublishedBy: null,
            PublishedAt: publishedAt,
            CreatedAt: publishedAt,
            UpdatedAt: publishedAt));
        dbContext.PublicationPackages.Add(new PublicationPackage(
            packageId,
            versionId,
            1,
            $"checksum-{packageId}",
            JsonDocument.Parse("{}"),
            "Published",
            publishedAt,
            publishedAt));
        dbContext.PublicationTargets.Add(new PublicationTarget(
            $"pt-{packageId}",
            packageId,
            "grade",
            "6",
            publishedAt,
            publishedAt));

        await dbContext.SaveChangesAsync();
    }

    private static async Task<PullResponse> PullAsync(SyncController controller, string? cursor, int limit = 50)
    {
        SyncTestPrincipals.BindNode(controller, "node-A");
        var result = await controller.Pull("node-A", cursor, limit, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PullResponse>(ok.Value);
    }

    private static SyncController CreateController(PlanCopeDbContext dbContext)
    {
        return new SyncController(dbContext, new CentralStatsRollupService(dbContext));
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }
}

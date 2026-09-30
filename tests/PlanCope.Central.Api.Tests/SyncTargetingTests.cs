using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the sync targeting contract: untargeted packages reach every node, node/school targeted
/// packages reach only matching nodes, grade/subject/division never filter delivery, and the cursor
/// advances past packages skipped by targeting without skipping a package the node must receive.
/// </summary>
public sealed class SyncTargetingTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Untargeted_package_is_delivered_to_any_node()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-1", 1, ("grade", "6"));
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, "node-B");

        var item = Assert.Single(response.Items);
        Assert.Equal("pkg-1", item.EntityId);
    }

    [Fact]
    public async Task Node_targeted_package_is_delivered_only_to_matching_node()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-node", 1, ("grade", "6"), ("node", "node-A"));
        var controller = CreateController(dbContext);

        var matched = await PullAsync(controller, "node-A");
        Assert.Equal("pkg-node", Assert.Single(matched.Items).EntityId);

        var unmatched = await PullAsync(controller, "node-B");
        Assert.Empty(unmatched.Items);
    }

    [Fact]
    public async Task School_targeted_package_matches_the_nodes_school_cue()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-school", 1, ("grade", "6"), ("school", "1001"));
        var controller = CreateController(dbContext);

        var matched = await PullAsync(controller, "node-A");
        Assert.Equal("pkg-school", Assert.Single(matched.Items).EntityId);

        var unmatched = await PullAsync(controller, "node-B");
        Assert.Empty(unmatched.Items);
    }

    [Fact]
    public async Task School_targeted_package_matches_by_internal_school_id()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedSchoolAsync(dbContext, "sch-1", 1001);
        await SeedPackageAsync(dbContext, "pkg-school-id", 1, ("grade", "6"), ("school", "sch-1"));
        var controller = CreateController(dbContext);

        var matched = await PullAsync(controller, "node-A");

        Assert.Equal("pkg-school-id", Assert.Single(matched.Items).EntityId);
    }

    [Fact]
    public async Task Universal_node_receives_all_school_targeted_packages()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-universal", string.Empty);
        await SeedSchoolAsync(dbContext, "sch-1", 1001);
        await SeedSchoolAsync(dbContext, "sch-2", 1002);
        await SeedPackageAsync(dbContext, "pkg-cue", 1, ("school", "1001"));
        await SeedPackageAsync(dbContext, "pkg-school-id", 2, ("school", "sch-2"));
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, "node-universal");

        Assert.Equal(new[] { "pkg-cue", "pkg-school-id" }, response.Items.Select(static item => item.EntityId));
    }

    [Fact]
    public async Task Unknown_node_still_receives_untargeted_packages()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedPackageAsync(dbContext, "pkg-1", 1, ("grade", "6"));
        await SeedPackageAsync(dbContext, "pkg-node", 2, ("node", "node-A"));
        var controller = CreateController(dbContext);

        var response = await PullAsync(controller, "unknown-node");

        Assert.Equal("pkg-1", Assert.Single(response.Items).EntityId);
    }

    [Fact]
    public async Task Cursor_advances_past_skipped_targeted_package_and_still_delivers_later_match()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-targeted", 1, ("node", "node-A"));
        await SeedPackageAsync(dbContext, "pkg-open", 2, ("grade", "6"));
        var controller = CreateController(dbContext);

        var first = await PullAsync(controller, "node-B", cursor: "0", limit: 1);
        Assert.Empty(first.Items);
        Assert.True(first.HasMore);
        Assert.NotEqual("0", first.NextCursor);

        var second = await PullAsync(controller, "node-B", cursor: first.NextCursor, limit: 10);
        Assert.Equal("pkg-open", Assert.Single(second.Items).EntityId);
        Assert.False(second.HasMore);
    }

    private static async Task SeedNodeAsync(PlanCopeDbContext dbContext, string nodeId, string cue, string? schoolId = null)
    {
        dbContext.RegisteredNodes.Add(new RegisteredNode(
            nodeId,
            schoolId,
            nodeId,
            DeviceName: null,
            Status: "Active",
            LastSeenAt: null,
            CreatedAt: BaseTime,
            UpdatedAt: BaseTime,
            FingerprintHash: $"fp-{nodeId}",
            FingerprintComponents: JsonDocument.Parse("{}"),
            Cue: cue,
            ActivationKeyId: null,
            EnrolledAt: BaseTime,
            RevokedAt: null,
            AppVersion: null));
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedSchoolAsync(PlanCopeDbContext dbContext, string schoolId, long cue)
    {
        dbContext.Schools.Add(new School(
            schoolId,
            $"CODE-{schoolId}",
            cue,
            Annex: null,
            Name: "Escuela",
            LocalityId: "loc-1",
            Status: "Active",
            DeletedAt: null,
            CreatedAt: BaseTime,
            UpdatedAt: BaseTime));
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPackageAsync(
        PlanCopeDbContext dbContext,
        string packageId,
        int publishedMinuteOffset,
        params (string Type, string? Id)[] targets)
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
            UpdatedAt: publishedAt,
            ScoringPolicy: null));
        dbContext.PublicationPackages.Add(new PublicationPackage(
            packageId,
            versionId,
            1,
            $"checksum-{packageId}",
            JsonDocument.Parse("{}"),
            "Published",
            publishedAt,
            publishedAt));

        var targetIndex = 0;
        foreach (var (type, id) in targets)
        {
            dbContext.PublicationTargets.Add(new PublicationTarget(
                $"pt-{packageId}-{targetIndex++}",
                packageId,
                type,
                id,
                publishedAt,
                publishedAt));
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<PullResponse> PullAsync(
        SyncController controller,
        string nodeId,
        string? cursor = null,
        int limit = 50)
    {
        SyncTestPrincipals.BindNode(controller, nodeId);
        var result = await controller.Pull(nodeId, cursor, limit, CancellationToken.None);
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

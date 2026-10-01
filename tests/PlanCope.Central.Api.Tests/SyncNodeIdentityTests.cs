using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the sync node-identity contract: the node id is always resolved from the validated JWT
/// (node_access + node_id claim), never from the query string, X-Node-Id header or request body.
/// A user/operator token is rejected with 403, a node token cannot act for another node, and the
/// claim value is what delivery and cursor writes use.
/// </summary>
public sealed class SyncNodeIdentityTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Pull_with_user_token_is_forbidden()
    {
        using var dbContext = CreateDbContext();
        await SeedPackageAsync(dbContext, "pkg-1", 1, ("grade", "6"));
        var controller = CreateController(dbContext);
        SyncTestPrincipals.Bind(controller, SyncTestPrincipals.User());

        var result = await controller.Pull("node-A", cursor: null, limit: 50, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Pull_with_node_token_and_foreign_nodeId_is_forbidden()
    {
        using var dbContext = CreateDbContext();
        await SeedPackageAsync(dbContext, "pkg-1", 1, ("grade", "6"));
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Pull("node-B", cursor: null, limit: 50, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Pull_without_nodeId_query_uses_the_claim_for_progress_tracking()
    {
        using var dbContext = CreateDbContext();
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-A", 1, ("node", "node-A"));
        await SeedPackageAsync(dbContext, "pkg-B", 2, ("node", "node-B"));
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Pull(nodeId: null, cursor: null, limit: 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PullResponse>(ok.Value);
        Assert.Equal(new[] { "pkg-A", "pkg-B" }, response.Items.Select(static item => item.EntityId));
    }

    [Fact]
    public async Task Pull_with_node_token_and_matching_nodeId_succeeds()
    {
        using var dbContext = CreateDbContext();
        await SeedPackageAsync(dbContext, "pkg-1", 1, ("grade", "6"));
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Pull("node-A", cursor: null, limit: 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PullResponse>(ok.Value);
        Assert.Equal("pkg-1", Assert.Single(response.Items).EntityId);
    }

    [Fact]
    public async Task Pull_with_revoked_node_is_forbidden()
    {
        using var dbContext = CreateDbContext();
        await SeedNodeAsync(dbContext, "node-A", "");
        var node = await dbContext.RegisteredNodes.SingleAsync(x => x.Id == "node-A");
        dbContext.Entry(node).CurrentValues.SetValues(node with { RevokedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Pull("node-A", cursor: null, limit: 50, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Pull_writes_delivery_and_cursor_markers_only_for_the_claim_node()
    {
        using var dbContext = CreateDbContext();
        await SeedNodeAsync(dbContext, "node-A", "1001");
        await SeedNodeAsync(dbContext, "node-B", "1002");
        await SeedPackageAsync(dbContext, "pkg-A", 1, ("node", "node-A"));
        await SeedPackageAsync(dbContext, "pkg-B", 2, ("node", "node-B"));
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        await controller.Pull(nodeId: null, cursor: null, limit: 50, CancellationToken.None);

        var cursors = await dbContext.SyncCursors.AsNoTracking().ToListAsync();
        Assert.Contains(cursors, cursor => cursor.NodeId == "node-A" && cursor.CursorKey == SyncCursorKeys.ExamPull);
        Assert.Contains(cursors, cursor => cursor.NodeId == "node-A" && cursor.CursorKey == "package:pkg-A");
        Assert.DoesNotContain(cursors, cursor => cursor.NodeId == "node-B");
        Assert.Contains(cursors, cursor => cursor.NodeId == "node-A" && cursor.CursorKey == "package:pkg-B");
    }

    [Fact]
    public async Task Push_with_user_token_is_forbidden()
    {
        using var dbContext = CreateDbContext();
        var request = new PushRequest("node-A", new[] { CreateItem("key-1", "exam_published", "ev-1") });
        var controller = CreateController(dbContext);
        SyncTestPrincipals.Bind(controller, SyncTestPrincipals.User());

        var result = await controller.Push(request, "node-A", new PushRequestValidator(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(dbContext.SyncInbox);
    }

    [Fact]
    public async Task Push_with_spoofed_nodeId_is_forbidden()
    {
        using var dbContext = CreateDbContext();
        var request = new PushRequest("node-B", new[] { CreateItem("key-1", "exam_published", "ev-1") });
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Push(request, "node-B", new PushRequestValidator(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(dbContext.SyncInbox);
    }

    [Fact]
    public async Task Push_with_matching_nodeId_persists_under_the_claim()
    {
        using var dbContext = CreateDbContext();
        var request = new PushRequest("node-A", new[] { CreateItem("key-1", "exam_published", "ev-1") });
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Push(request, "node-A", new PushRequestValidator(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PushResponse>(ok.Value);
        Assert.Equal(1, response.Received);
        var inbox = Assert.Single(dbContext.SyncInbox);
        Assert.Equal("node-A", inbox.SourceNodeId);
    }

    [Fact]
    public async Task Push_from_revoked_node_still_accepts_already_collected_results()
    {
        using var dbContext = CreateDbContext();
        await SeedNodeAsync(dbContext, "node-revoked", "180000100");
        var node = await dbContext.RegisteredNodes.SingleAsync();
        dbContext.Entry(node).CurrentValues.SetValues(node with { RevokedAt = BaseTime });
        await dbContext.SaveChangesAsync();
        var request = new PushRequest("node-revoked", new[] { CreateItem("revoked-result", "exam_published", "ev-1") });
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-revoked");

        var result = await controller.Push(request, "node-revoked", new PushRequestValidator(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("node-revoked", Assert.Single(dbContext.SyncInbox).SourceNodeId);
    }

    [Fact]
    public async Task Push_with_header_and_body_mismatch_still_returns_400_after_the_claim_check()
    {
        using var dbContext = CreateDbContext();
        var request = new PushRequest("node-B", new[] { CreateItem("key-1", "exam_published", "ev-1") });
        var controller = CreateController(dbContext);
        SyncTestPrincipals.BindNode(controller, "node-A");

        var result = await controller.Push(request, "node-A", new PushRequestValidator(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static PushItem CreateItem(string idempotencyKey, string eventType, string aggregateId)
    {
        var payload = JsonSerializer.SerializeToElement(new { versionId = aggregateId });
        return new PushItem(
            idempotencyKey,
            eventType,
            "exam",
            aggregateId,
            payload,
            SyncPayloadChecksum.Calculate(payload),
            "2026-01-01T00:00:00+00:00");
    }

    private static async Task SeedNodeAsync(PlanCopeDbContext dbContext, string nodeId, string cue)
    {
        dbContext.RegisteredNodes.Add(new RegisteredNode(
            nodeId,
            SchoolId: null,
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
            Courses: [],
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

    private static SyncController CreateController(PlanCopeDbContext dbContext) =>
        new(dbContext, new CentralStatsRollupService(dbContext));

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PlanCopeDbContext(options);
    }
}

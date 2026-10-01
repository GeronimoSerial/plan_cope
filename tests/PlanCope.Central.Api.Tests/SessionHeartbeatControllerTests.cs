using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class SessionHeartbeatControllerTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Heartbeat_requires_node_token_and_rejects_foreign_cue()
    {
        using var db = CreateDbContext();
        await SeedNodeAsync(db, "node-A", "180055400");
        var controller = new SessionHeartbeatController(db);
        Bind(controller, "user", "node-A");
        var result = await controller.Receive(Request("session-1", "180055400"), "node-A", CancellationToken.None);
        Assert.IsType<ForbidResult>(result);

        Bind(controller, "node_access", "node-A");
        result = await controller.Receive(Request("session-1", "180055401"), "node-A", CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Heartbeat_upserts_only_the_calling_nodes_session()
    {
        using var db = CreateDbContext();
        await SeedNodeAsync(db, "node-A", "180055400");
        await SeedNodeAsync(db, "node-B", "180055400");
        var controller = new SessionHeartbeatController(db);
        Bind(controller, "node_access", "node-A");
        Assert.IsType<OkObjectResult>(await controller.Receive(Request("session-1", "180055400") with { InProgressCount = 2 }, "node-A", CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Receive(Request("session-1", "180055400") with { SubmittedCount = 1, InProgressCount = 1 }, "node-A", CancellationToken.None));

        var sessions = await db.DeliverySessions.ToListAsync();
        var session = Assert.Single(sessions);
        Assert.Equal("node-A", session.SourceNodeId);
        Assert.Equal("180055400", session.SchoolId);
        Assert.Equal(1, session.SubmittedCount);
        Assert.Equal(1, session.InProgressCount);
        Assert.NotNull(session.LastHeartbeatAt);
    }

    [Fact]
    public async Task Admin_list_marks_old_nonclosed_heartbeats_as_sin_senal()
    {
        using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        db.DeliverySessions.AddRange(
            Session("fresh", "node-A", now.AddMinutes(-2)),
            Session("stale", "node-A", now.AddMinutes(-12)));
        await db.SaveChangesAsync();
        var controller = new LiveSessionsAdminController(db);
        controller.ControllerContext.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"))
        };

        var result = await controller.List(CancellationToken.None);
        var rows = Assert.IsType<OkObjectResult>(result.Result).Value as IReadOnlyList<LiveSessionSummary>;
        Assert.NotNull(rows);
        Assert.Equal("En curso", rows.Single(row => row.SessionId == "fresh").SignalStatus);
        Assert.Equal("Sin señal", rows.Single(row => row.SessionId == "stale").SignalStatus);
    }

    private static SessionHeartbeatRequest Request(string sessionId, string cue) => new(
        sessionId, cue, "2026", "section-1", "exam-1", "active", 4, 2, 1, 1,
        DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, "1.0.0");

    private static CentralDeliverySession Session(string id, string nodeId, DateTimeOffset? heartbeat) => new(
        id, id, "180055400", "exam-1", null, null, "active", DateTimeOffset.UtcNow.AddHours(-1), null,
        heartbeat, DateTimeOffset.UtcNow.AddHours(-1), nodeId, "2026", "section-1", 5, 2, 3, 0,
        DateTimeOffset.UtcNow, heartbeat, "1.0.0");

    private static async Task SeedNodeAsync(PlanCopeDbContext db, string nodeId, string cue)
    {
        db.RegisteredNodes.Add(new RegisteredNode(nodeId, null, nodeId, null, "Active", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, $"fp-{nodeId}", JsonDocument.Parse("{}"), cue,
            null, DateTimeOffset.UtcNow, null, null));
        await db.SaveChangesAsync();
    }

    private static void Bind(ControllerBase controller, string tokenType, string nodeId) =>
        controller.ControllerContext.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("token_type", tokenType), new Claim("node_id", nodeId)
            ], "test"))
        };

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

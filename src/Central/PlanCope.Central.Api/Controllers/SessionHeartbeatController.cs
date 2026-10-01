using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync/session-heartbeat")]
public sealed class SessionHeartbeatController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] SessionHeartbeatRequest request,
        [FromHeader(Name = "X-Node-Id")] string? nodeHeader, CancellationToken cancellationToken)
    {
        if (!NodeAccessAuth.TryGetNodeId(User, out var claimNodeId)) return Forbid();
        if (string.IsNullOrWhiteSpace(nodeHeader) || !string.Equals(nodeHeader.Trim(), claimNodeId, StringComparison.Ordinal))
            return BadRequest(new { error = "X-Node-Id must match the node access token." });
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 128 ||
            !CueCode.TryNormalize(request.Cue, out var cue) ||
            request.Status is not ("active" or "paused") ||
            request.JoinedCount < 0 || request.InProgressCount < 0 || request.SubmittedCount < 0 || request.ClosedOrForcedCount < 0 ||
            request.JoinedCount > 100000 || request.InProgressCount > request.JoinedCount || request.SubmittedCount > request.JoinedCount ||
            request.SchoolYear?.Length > 32 || request.RosterSectionId?.Length > 128 || request.ExamVersionId?.Length > 128 || request.AppVersion?.Length > 64)
            return BadRequest(new { error = "The session heartbeat is invalid." });

        var node = await dbContext.RegisteredNodes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == claimNodeId, cancellationToken);
        if (node is null || !CueCode.TryNormalize(node.Cue, out var nodeCue) || !string.Equals(nodeCue, cue, StringComparison.Ordinal))
            return Forbid();

        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.DeliverySessions.SingleOrDefaultAsync(session =>
            session.SourceNodeId == claimNodeId && session.RemoteLocalId == request.SessionId, cancellationToken);
        if (existing?.Status == "closed") return Conflict(new { error = "The session is already closed." });
        var session = new CentralDeliverySession(
            existing?.Id ?? Guid.NewGuid().ToString("N"), request.SessionId, cue,
            existing?.ExamVersionId ?? request.ExamVersionId, existing?.ClassroomCode, existing?.CommissionCode,
            request.Status, existing?.StartedAt ?? request.StartedAt, existing?.EndedAt, now,
            existing?.CreatedAt ?? now, claimNodeId, request.SchoolYear, request.RosterSectionId,
            request.JoinedCount, request.InProgressCount, request.SubmittedCount, request.ClosedOrForcedCount,
            request.LastActivityAt, now, request.AppVersion);
        if (existing is null) dbContext.DeliverySessions.Add(session);
        else dbContext.Entry(existing).CurrentValues.SetValues(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { receivedAt = now });
    }
}

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/live-sessions")]
public sealed class LiveSessionsAdminController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LiveSessionSummary>>> List(CancellationToken cancellationToken)
    {
        var sessions = await dbContext.DeliverySessions.AsNoTracking()
            .Where(session => session.Status == "active" || session.Status == "paused")
            .OrderByDescending(session => session.LastHeartbeatAt)
            .ThenByDescending(session => session.StartedAt)
            .ToListAsync(cancellationToken);
        var staleCutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        return Ok(sessions.Select(session => new LiveSessionSummary(
            session.RemoteLocalId, session.SchoolId ?? "", session.SchoolYear, session.RosterSectionId,
            session.ExamVersionId, session.Status, session.JoinedCount, session.InProgressCount,
            session.SubmittedCount, session.ClosedOrForcedCount, session.StartedAt ?? session.CreatedAt,
            session.LastActivityAt, session.LastHeartbeatAt,
            session.LastHeartbeatAt is null || session.LastHeartbeatAt < staleCutoff ? "Sin señal" : "En curso",
            session.LocalAppVersion)).ToList());
    }
}

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
        if (node is null || node.RevokedAt is not null || !CueCode.TryNormalize(node.Cue, out var nodeCue) || !string.Equals(nodeCue, cue, StringComparison.Ordinal))
            return Forbid();

        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.DeliverySessions.SingleOrDefaultAsync(session =>
            session.SourceNodeId == claimNodeId && session.RemoteLocalId == request.SessionId, cancellationToken);
        if (existing?.Status == "closed") return Conflict(new { error = "The session is already closed." });
        var reportedSentAt = request.SentAt == default ? now : request.SentAt;
        if (existing?.LastHeartbeatAt is { } previousHeartbeat && previousHeartbeat >= reportedSentAt)
            return Ok(new { receivedAt = previousHeartbeat });
        var sentAt = reportedSentAt >= now.AddMinutes(-10) && reportedSentAt <= now.AddMinutes(10)
            ? reportedSentAt
            : now;
        var session = new CentralDeliverySession(
            existing?.Id ?? Guid.NewGuid().ToString("N"), request.SessionId, cue,
            existing?.ExamVersionId ?? request.ExamVersionId, existing?.ClassroomCode, existing?.CommissionCode,
            request.Status, existing?.StartedAt ?? request.StartedAt, existing?.EndedAt, now,
            existing?.CreatedAt ?? now, claimNodeId, request.SchoolYear, existing?.Course, request.RosterSectionId,
            request.JoinedCount, request.InProgressCount, request.SubmittedCount, request.ClosedOrForcedCount,
            request.LastActivityAt, sentAt, request.AppVersion);
        if (existing is null) dbContext.DeliverySessions.Add(session);
        else dbContext.Entry(existing).CurrentValues.SetValues(session);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            dbContext.Entry(session).State = EntityState.Detached;
            var raced = await dbContext.DeliverySessions.SingleOrDefaultAsync(item =>
                item.SourceNodeId == claimNodeId && item.RemoteLocalId == request.SessionId, cancellationToken);
            if (raced is null) return Ok(new { receivedAt = sentAt });
            if (raced.Status == "closed") return Conflict(new { error = "The session is already closed." });
            if (raced.LastHeartbeatAt is null || raced.LastHeartbeatAt < reportedSentAt)
            {
                var retry = session with { Id = raced.Id, CreatedAt = raced.CreatedAt, Course = raced.Course };
                dbContext.Entry(raced).CurrentValues.SetValues(retry);
                try { await dbContext.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException) { return Ok(new { receivedAt = raced.LastHeartbeatAt ?? sentAt }); }
            }
        }
        return Ok(new { receivedAt = sentAt });
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
        var staleCutoff = DateTimeOffset.UtcNow - SessionHeartbeatPolicy.StaleAfter;
        return Ok(sessions.Select(session => new LiveSessionSummary(
            session.RemoteLocalId, session.SchoolId ?? "", session.SchoolYear, session.RosterSectionId,
            session.ExamVersionId, session.Status, session.JoinedCount, session.InProgressCount,
            session.SubmittedCount, session.ClosedOrForcedCount, session.StartedAt ?? session.CreatedAt,
            session.LastActivityAt, session.LastHeartbeatAt,
            session.LastHeartbeatAt is null || session.LastHeartbeatAt < staleCutoff ? "Sin señal" : "En curso",
            session.LocalAppVersion)).ToList());
    }
}

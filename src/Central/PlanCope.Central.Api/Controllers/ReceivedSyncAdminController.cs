using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/sync/received")]
public sealed class ReceivedSyncAdminController(
    PlanCopeDbContext dbContext,
    CentralStatsRollupService statsRollupService,
    ILogger<ReceivedSyncAdminController>? logger = null,
    CentralAttemptGradingService? attemptGradingService = null) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.ReceivedStudentAttempts.AsNoTracking().OrderByDescending(attempt => attempt.ReceivedAt).ThenByDescending(attempt => attempt.Id);
        var totalCount = await query.CountAsync(cancellationToken);
        var attempts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var attemptIds = attempts.Select(attempt => attempt.Id).ToList();
        var idempotencyKeys = attempts.Select(attempt => attempt.IdempotencyKey).ToList();
        var sessionIds = attempts.Where(attempt => attempt.DeliverySessionId is not null).Select(attempt => attempt.DeliverySessionId!).ToList();

        var sessions = await dbContext.DeliverySessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id)).ToDictionaryAsync(session => session.Id, cancellationToken);
        var grades = await dbContext.CentralAttemptResults.AsNoTracking()
            .Where(result => attemptIds.Contains(result.ReceivedStudentAttemptId)).ToListAsync(cancellationToken);
        var latestGrades = grades.GroupBy(result => result.ReceivedStudentAttemptId)
            .ToDictionary(group => group.Key, group => group.MaxBy(result => result.GradedAt)!);
        var inbox = await dbContext.SyncInbox.AsNoTracking()
            .Where(item => idempotencyKeys.Contains(item.IdempotencyKey))
            .ToListAsync(cancellationToken);

        var items = attempts.Select(attempt =>
        {
            sessions.TryGetValue(attempt.DeliverySessionId ?? string.Empty, out var session);
            latestGrades.TryGetValue(attempt.Id, out var grade);
            var inboxItem = inbox.FirstOrDefault(item => item.IdempotencyKey == attempt.IdempotencyKey);
            var payload = inboxItem?.Payload.RootElement;
            var examVersionId = session?.ExamVersionId ?? (payload is { } value ? ReadOptionalString(value, "examVersionRemoteId") : null);
            return new
            {
                attemptId = attempt.Id,
                receivedAt = attempt.ReceivedAt,
                nodeId = session?.SourceNodeId ?? inboxItem?.SourceNodeId,
                cue = session?.SchoolId,
                schoolYear = session?.SchoolYear,
                rosterSectionId = attempt.RosterSectionId,
                examVersionId,
                gradingStatus = grade?.Status ?? "pending",
                gradingReason = grade?.Reason,
                attributionStatus = attempt.AttributionStatus,
                attributionReason = attempt.AttributionReason
            };
        }).ToList();

        return Ok(new { page, pageSize, totalCount, items });
    }

    [HttpPost("reprocess")]
    public async Task<ActionResult> Reprocess(CancellationToken cancellationToken)
    {
        logger?.LogInformation("Administrator started reprocessing received Central attempts.");
        var attempts = await dbContext.ReceivedStudentAttempts.AsNoTracking()
            .OrderBy(attempt => attempt.Id).ToListAsync(cancellationToken);
        var latestGrades = (await dbContext.CentralAttemptResults.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(result => result.ReceivedStudentAttemptId)
            .ToDictionary(group => group.Key, group => group.MaxBy(result => result.GradedAt)!);
        var grader = attemptGradingService ?? new CentralAttemptGradingService(dbContext, statsRollupService);
        var reprocessed = 0;

        foreach (var attempt in attempts)
        {
            latestGrades.TryGetValue(attempt.Id, out var latestGrade);
            if (attempt.AttributionStatus == "attributed" && latestGrade?.Status == "graded") continue;

            var examVersionId = await ResolveExamVersionIdAsync(attempt, cancellationToken);
            var answers = await dbContext.ReceivedSubmissionAnswers.AsNoTracking()
                .Where(answer => answer.StudentAttemptId == attempt.RemoteLocalId)
                .ToListAsync(cancellationToken);
            await grader.RecomputeAsync(attempt.Id, examVersionId, answers, cancellationToken, updateRollup: false);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            reprocessed++;
        }

        try
        {
            var rebuilt = await statsRollupService.RebuildAllAsync(cancellationToken);
            logger?.LogInformation("Received attempt reprocessing completed: {ReprocessedCount} graded again, {RollupCount} rollups rebuilt.", reprocessed, rebuilt);
            return Ok(new { reprocessed, rebuilt });
        }
        catch (StatsRollupRebuildInProgressException)
        {
            return Conflict(new { error = "Another statistics rebuild is in progress. Try again shortly." });
        }
    }

    private async Task<string?> ResolveExamVersionIdAsync(ReceivedStudentAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.DeliverySessionId is not null)
        {
            var session = await dbContext.DeliverySessions.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == attempt.DeliverySessionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(session?.ExamVersionId)) return session.ExamVersionId;
        }

        var item = await dbContext.SyncInbox.AsNoTracking()
            .FirstOrDefaultAsync(value => value.IdempotencyKey == attempt.IdempotencyKey, cancellationToken);
        return item is null ? null : ReadOptionalString(item.Payload.RootElement, "examVersionRemoteId");
    }

    private static string? ReadOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

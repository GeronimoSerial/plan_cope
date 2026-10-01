using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;
using PlanCope.Shared.Grading;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlanCope.Central.Api.Services;

public sealed class CentralStatsRollupService(PlanCopeDbContext dbContext, ILogger<CentralStatsRollupService> logger)
{
    private const string UnassignedCourse = "sin_asignar";
    private static readonly JsonSerializerOptions RollupJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public CentralStatsRollupService(PlanCopeDbContext dbContext)
        : this(dbContext, Microsoft.Extensions.Logging.Abstractions.NullLogger<CentralStatsRollupService>.Instance)
    {
    }

    public async Task UpsertForAttemptAsync(
        string receivedStudentAttemptId,
        PlanCope.Shared.Grading.AttemptResult result,
        string examVersionId,
        CancellationToken cancellationToken = default)
    {
        // The attempt is added to the change tracker by the caller before SaveChangesAsync, so a
        // tracker-aware lookup is required here; a plain query would not see the pending row.
        var attempt = await dbContext.ReceivedStudentAttempts
            .FindAsync([receivedStudentAttemptId], cancellationToken);
        if (attempt is null || attempt.RosterSectionId is null)
        {
            logger.LogInformation("Cannot attribute Central attempt {AttemptId}: delivery session or roster section is missing.", receivedStudentAttemptId);
            return;
        }

        var section = await dbContext.GeRosterSections
            .SingleOrDefaultAsync(x => x.Id == attempt.RosterSectionId, cancellationToken);
        if (section is null)
        {
            logger.LogInformation("Cannot attribute Central attempt {AttemptId}: roster section {RosterSectionId} was not found.", receivedStudentAttemptId, attempt.RosterSectionId);
            return;
        }

        var snapshot = await dbContext.GeRosterSnapshots
            .SingleOrDefaultAsync(x => x.Id == section.SnapshotId, cancellationToken);
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.SchoolYear) || !CueCode.TryNormalize(snapshot.Cue, out var rosterCue))
        {
            logger.LogInformation("Cannot attribute Central attempt {AttemptId}: roster snapshot {RosterSnapshotId} has no valid CUE or school year.", receivedStudentAttemptId, section.SnapshotId);
            return;
        }

        var deliverySession = attempt.DeliverySessionId is null
            ? null
            : dbContext.DeliverySessions.Local.FirstOrDefault(x => x.Id == attempt.DeliverySessionId)
                ?? await dbContext.DeliverySessions.SingleOrDefaultAsync(x => x.Id == attempt.DeliverySessionId, cancellationToken);
        var cue = CueCode.TryNormalize(deliverySession?.SchoolId, out var sessionCue) ? sessionCue : rosterCue;
        if (deliverySession?.SchoolId is not null && cue == rosterCue && !CueCode.TryNormalize(deliverySession.SchoolId, out _))
        {
            logger.LogDebug("Central attempt {AttemptId} has an invalid session CUE; using roster snapshot {RosterSnapshotId}.", receivedStudentAttemptId, section.SnapshotId);
        }

        var schoolYear = snapshot.SchoolYear;
        var course = string.IsNullOrWhiteSpace(section.Course) ? UnassignedCourse : section.Course;
        var now = DateTimeOffset.UtcNow;

        var rollup = await dbContext.ExamRollups
            .SingleOrDefaultAsync(
                x => x.Cue == cue && x.SchoolYear == schoolYear && x.Course == course && x.ExamVersionId == examVersionId,
                cancellationToken);

        string rollupId;
        if (rollup is null)
        {
            rollupId = Guid.NewGuid().ToString("N");
            dbContext.ExamRollups.Add(new ExamRollup(
                rollupId,
                cue,
                schoolYear,
                course,
                examVersionId,
                AttemptCount: 1,
                ScoreSum: (double)result.Score,
                ScoreMaxSum: (double)result.ScoreMax,
                UpdatedAt: now));
        }
        else
        {
            rollupId = rollup.Id;
            dbContext.Entry(rollup).CurrentValues.SetValues(rollup with
            {
                AttemptCount = rollup.AttemptCount + 1,
                ScoreSum = rollup.ScoreSum + (double)result.Score,
                ScoreMaxSum = rollup.ScoreMaxSum + (double)result.ScoreMax,
                UpdatedAt = now
            });
        }

        foreach (var block in result.Blocks)
        {
            var correct = block.Outcome == BlockOutcome.Correct ? 1 : 0;
            var partial = block.Outcome == BlockOutcome.Partial ? 1 : 0;
            var incorrect = block.Outcome == BlockOutcome.Incorrect ? 1 : 0;
            var blank = block.Outcome == BlockOutcome.Blank ? 1 : 0;
            var ungradable = block.Outcome == BlockOutcome.Ungradable ? 1 : 0;

            var existingBlock = await dbContext.ExamRollupBlocks
                .SingleOrDefaultAsync(
                    x => x.RollupId == rollupId && x.BlockId == block.BlockId,
                    cancellationToken);
            if (existingBlock is null)
            {
                dbContext.ExamRollupBlocks.Add(new ExamRollupBlock(
                    Guid.NewGuid().ToString("N"),
                    rollupId,
                    block.BlockId,
                    correct,
                    partial,
                    incorrect,
                    blank,
                    ungradable,
                    (double)block.Score,
                    (double)block.ScoreMax));
            }
            else
            {
                dbContext.Entry(existingBlock).CurrentValues.SetValues(existingBlock with
                {
                    CorrectCount = existingBlock.CorrectCount + correct,
                    PartialCount = existingBlock.PartialCount + partial,
                    IncorrectCount = existingBlock.IncorrectCount + incorrect,
                    BlankCount = existingBlock.BlankCount + blank,
                    UngradableCount = existingBlock.UngradableCount + ungradable,
                    ScoreSum = existingBlock.ScoreSum + (double)block.Score,
                    ScoreMaxSum = existingBlock.ScoreMaxSum + (double)block.ScoreMax
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RebuildAllAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var attempts = await dbContext.ReceivedStudentAttempts.AsNoTracking().ToListAsync(cancellationToken);
        var storedResults = await dbContext.CentralAttemptResults.AsNoTracking()
            .Where(result => result.Status == "graded" && result.Score != null && result.ScoreMax != null && result.BlocksJson != null)
            .ToListAsync(cancellationToken);
        var results = storedResults
            .GroupBy(result => result.ReceivedStudentAttemptId)
            .Select(group => group.MaxBy(result => result.GradedAt)!)
            .OrderBy(result => result.GradedAt)
            .ToList();

        dbContext.ExamRollupBlocks.RemoveRange(await dbContext.ExamRollupBlocks.ToListAsync(cancellationToken));
        dbContext.ExamRollups.RemoveRange(await dbContext.ExamRollups.ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);

        var attemptsById = attempts.ToDictionary(attempt => attempt.Id);
        var rebuilt = 0;
        foreach (var resultRow in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!attemptsById.TryGetValue(resultRow.ReceivedStudentAttemptId, out var attempt)) continue;
            List<BlockResult>? blocks;
            try
            {
                blocks = resultRow.BlocksJson!.RootElement.Deserialize<List<BlockResult>>(RollupJsonOptions);
            }
            catch (JsonException)
            {
                logger.LogWarning("Skipping Central rollup rebuild for attempt {AttemptId}: stored grade blocks could not be read.", attempt.Id);
                continue;
            }

            if (blocks is null) continue;
            var examVersionId = attempt.DeliverySessionId is null
                ? null
                : await dbContext.DeliverySessions.AsNoTracking()
                    .Where(session => session.Id == attempt.DeliverySessionId)
                    .Select(session => session.ExamVersionId)
                    .SingleOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(examVersionId))
            {
                var inboxPayload = await dbContext.SyncInbox.AsNoTracking()
                    .Where(inbox => inbox.AggregateId == attempt.RemoteLocalId)
                    .Select(inbox => inbox.Payload)
                    .SingleOrDefaultAsync(cancellationToken);
                if (inboxPayload is not null && inboxPayload.RootElement.TryGetProperty("examVersionRemoteId", out var versionElement))
                {
                    examVersionId = versionElement.GetString();
                }
            }
            if (string.IsNullOrWhiteSpace(examVersionId))
            {
                logger.LogInformation("Cannot rebuild Central rollup for attempt {AttemptId}: exam version is missing.", attempt.Id);
                continue;
            }

            await UpsertForAttemptAsync(attempt.Id, new AttemptResult
            {
                ExamVersionId = examVersionId,
                Score = resultRow.Score!.Value,
                ScoreMax = resultRow.ScoreMax!.Value,
                Blocks = blocks
            }, examVersionId, cancellationToken);
            rebuilt++;
        }

        await transaction.CommitAsync(cancellationToken);
        return rebuilt;
    }
}

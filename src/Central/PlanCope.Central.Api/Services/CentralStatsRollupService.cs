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
    internal const int AdvisoryLockNamespace = 1_284_731_029;
    internal const int AdvisoryLockKey = 1;
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int RebuildBatchSize = 250;
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
        if (attempt is null)
        {
            logger.LogWarning("Cannot attribute Central attempt {AttemptId}: received attempt was not found.", receivedStudentAttemptId);
            return;
        }

        var deliverySession = attempt.DeliverySessionId is null
            ? null
            : dbContext.DeliverySessions.Local.FirstOrDefault(x => x.Id == attempt.DeliverySessionId)
                ?? await dbContext.DeliverySessions.SingleOrDefaultAsync(x => x.Id == attempt.DeliverySessionId, cancellationToken);
        var section = string.IsNullOrWhiteSpace(attempt.RosterSectionId)
            ? null
            : await dbContext.GeRosterSections.SingleOrDefaultAsync(x => x.Id == attempt.RosterSectionId, cancellationToken);
        GeRosterSnapshot? snapshot = section is null
            ? null
            : await dbContext.GeRosterSnapshots.SingleOrDefaultAsync(x => x.Id == section.SnapshotId, cancellationToken);
        if (snapshot is null && !string.IsNullOrWhiteSpace(attempt.RosterSnapshotId))
        {
            snapshot = await dbContext.GeRosterSnapshots.SingleOrDefaultAsync(x => x.Id == attempt.RosterSnapshotId, cancellationToken);
        }

        var hasRosterCue = CueCode.TryNormalize(snapshot?.Cue, out var rosterCue);
        var hasSessionCue = CueCode.TryNormalize(deliverySession?.SchoolId, out var sessionCue);
        string cue;
        if (hasRosterCue)
        {
            cue = rosterCue;
            if (!string.IsNullOrWhiteSpace(deliverySession?.SchoolId) && (!hasSessionCue || sessionCue != rosterCue))
            {
                logger.LogWarning("Central attempt {AttemptId} session CUE {SessionCue} differs from roster CUE {RosterCue}; using the roster CUE.", receivedStudentAttemptId, deliverySession!.SchoolId, rosterCue);
            }
        }
        else if (hasSessionCue && await IsKnownCueAsync(sessionCue, cancellationToken))
        {
            cue = sessionCue;
        }
        else
        {
            await MarkAttributionAsync(attempt, "unattributed", "Neither the roster snapshot nor the delivery session contains a known CUE.", cancellationToken);
            return;
        }

        var schoolYear = !string.IsNullOrWhiteSpace(snapshot?.SchoolYear)
            ? snapshot.SchoolYear
            : !string.IsNullOrWhiteSpace(deliverySession?.SchoolYear)
                ? deliverySession.SchoolYear
                : UnassignedCourse;
        var courseValue = section?.Course ?? deliverySession?.Course;
        var course = string.IsNullOrWhiteSpace(courseValue) ? UnassignedCourse : courseValue;
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

        await MarkAttributionAsync(attempt, "attributed", null, cancellationToken, saveChanges: false);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkAttributionAsync(
        ReceivedStudentAttempt attempt,
        string status,
        string? reason,
        CancellationToken cancellationToken,
        bool saveChanges = true)
    {
        if (status != "attributed")
        {
            logger.LogWarning("Cannot attribute Central attempt {AttemptId}: {Reason}", attempt.Id, reason);
        }
        dbContext.Entry(attempt).CurrentValues.SetValues(attempt with
        {
            AttributionStatus = status,
            AttributionReason = reason
        });
        if (saveChanges) await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsKnownCueAsync(string cue, CancellationToken cancellationToken)
    {
        if (await dbContext.GeRosterSnapshots.AsNoTracking().AnyAsync(snapshot => snapshot.Cue == cue, cancellationToken))
        {
            return true;
        }

        if (!long.TryParse(cue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fullCue) ||
            !long.TryParse(cue[..7], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var baseCue)) return false;
        var schools = await dbContext.Schools.AsNoTracking()
            .Where(school => school.Cue == fullCue || school.Cue == baseCue)
            .Select(school => new { school.Cue, school.Annex })
            .ToListAsync(cancellationToken);
        return schools.Any(school => CueCode.TryFromSchool(school.Cue, school.Annex, out var knownCue) && knownCue == cue);
    }

    public async Task<int> RebuildAllAsync(CancellationToken cancellationToken = default)
    {
        var ownsPostgresLock = await TryAcquireRebuildLockAsync(cancellationToken);
        if (dbContext.Database.ProviderName == NpgsqlProviderName && !ownsPostgresLock)
        {
            throw new StatsRollupRebuildInProgressException();
        }

        try
        {
            if (dbContext.Database.ProviderName == NpgsqlProviderName)
            {
                await dbContext.ExamRollupBlocks.ExecuteDeleteAsync(cancellationToken);
                await dbContext.ExamRollups.ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                dbContext.ExamRollupBlocks.RemoveRange(await dbContext.ExamRollupBlocks.ToListAsync(cancellationToken));
                dbContext.ExamRollups.RemoveRange(await dbContext.ExamRollups.ToListAsync(cancellationToken));
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            dbContext.ChangeTracker.Clear();

            string? cursor = null;
            var rebuilt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = dbContext.ReceivedStudentAttempts.AsNoTracking().OrderBy(attempt => attempt.Id);
                if (cursor is not null) query = query.Where(attempt => string.Compare(attempt.Id, cursor) > 0).OrderBy(attempt => attempt.Id);
                var attempts = await query.Take(RebuildBatchSize).ToListAsync(cancellationToken);
                if (attempts.Count == 0) break;
                cursor = attempts[^1].Id;

                var attemptIds = attempts.Select(attempt => attempt.Id).ToList();
                var resultRows = await dbContext.CentralAttemptResults.AsNoTracking()
                    .Where(result => attemptIds.Contains(result.ReceivedStudentAttemptId))
                    .ToListAsync(cancellationToken);
                var resultsByAttempt = resultRows.GroupBy(result => result.ReceivedStudentAttemptId)
                    .ToDictionary(group => group.Key, group => group.MaxBy(result => result.GradedAt)!);

                await using var batchTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                foreach (var attempt in attempts)
                {
                    if (!resultsByAttempt.TryGetValue(attempt.Id, out var resultRow))
                    {
                        await MarkAttributionAsync(attempt, "unattributed", "No grading result is stored for this received attempt.", cancellationToken);
                        continue;
                    }
                    if (!string.Equals(resultRow.Status, "graded", StringComparison.Ordinal) ||
                        resultRow.Score is null || resultRow.ScoreMax is null || resultRow.BlocksJson is null)
                    {
                        await MarkAttributionAsync(attempt, "unattributed", resultRow.Reason ?? "The latest grading result is not gradable.", cancellationToken);
                        continue;
                    }
                    List<BlockResult>? blocks;
                    try
                    {
                        blocks = resultRow.BlocksJson!.RootElement.Deserialize<List<BlockResult>>(RollupJsonOptions);
                    }
                    catch (JsonException)
                    {
                        await MarkAttributionAsync(attempt, "unattributed", "Stored grade blocks could not be read.", cancellationToken);
                        continue;
                    }

                    if (blocks is null)
                    {
                        await MarkAttributionAsync(attempt, "unattributed", "Stored grade blocks are empty.", cancellationToken);
                        continue;
                    }
                    var examVersionId = await ResolveExamVersionIdAsync(attempt, cancellationToken);
                    if (string.IsNullOrWhiteSpace(examVersionId))
                    {
                        await MarkAttributionAsync(attempt, "unattributed", "Exam version is missing from the attempt and its inbox item.", cancellationToken);
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

                await batchTransaction.CommitAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }

            return rebuilt;
        }
        finally
        {
            if (ownsPostgresLock) await ReleaseRebuildLockAsync();
        }
    }

    private async Task<string?> ResolveExamVersionIdAsync(ReceivedStudentAttempt attempt, CancellationToken cancellationToken)
    {
        CentralDeliverySession? session = null;
        if (attempt.DeliverySessionId is not null)
        {
            session = await dbContext.DeliverySessions.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == attempt.DeliverySessionId, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(session?.ExamVersionId)) return session.ExamVersionId;

        var inboxQuery = dbContext.SyncInbox.AsNoTracking()
            .Where(inbox => inbox.IdempotencyKey == attempt.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(session?.SourceNodeId))
        {
            inboxQuery = inboxQuery.Where(inbox => inbox.SourceNodeId == session.SourceNodeId);
        }
        var inboxItem = await inboxQuery.OrderBy(inbox => inbox.CreatedAt).ThenBy(inbox => inbox.Id).FirstOrDefaultAsync(cancellationToken);
        if (inboxItem is not null && inboxItem.Payload.RootElement.TryGetProperty("examVersionRemoteId", out var versionElement))
        {
            return versionElement.GetString();
        }

        return null;
    }

    private async Task<bool> TryAcquireRebuildLockAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName != NpgsqlProviderName) return false;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock({AdvisoryLockNamespace}, {AdvisoryLockKey});";
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!acquired) await dbContext.Database.CloseConnectionAsync();
            return acquired;
        }
        catch
        {
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }
    }

    private async Task ReleaseRebuildLockAsync()
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT pg_advisory_unlock({AdvisoryLockNamespace}, {AdvisoryLockKey});";
        await command.ExecuteScalarAsync();
        await dbContext.Database.CloseConnectionAsync();
    }
}

public sealed class StatsRollupRebuildInProgressException : Exception
{
}

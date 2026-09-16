using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Grading;

namespace PlanCope.Central.Api.Services;

public sealed class CentralStatsRollupService(PlanCopeDbContext dbContext)
{
    private const string UnassignedCourse = "sin_asignar";

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
        if (attempt?.DeliverySessionId is null || attempt.RosterSectionId is null)
        {
            return;
        }

        var deliverySession = await dbContext.DeliverySessions
            .SingleOrDefaultAsync(x => x.Id == attempt.DeliverySessionId, cancellationToken);
        if (deliverySession?.SchoolId is null)
        {
            return;
        }

        var section = await dbContext.GeRosterSections
            .SingleOrDefaultAsync(x => x.Id == attempt.RosterSectionId, cancellationToken);
        if (section is null)
        {
            return;
        }

        var snapshot = await dbContext.GeRosterSnapshots
            .SingleOrDefaultAsync(x => x.Id == section.SnapshotId, cancellationToken);
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.SchoolYear))
        {
            return;
        }

        var cue = deliverySession.SchoolId;
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
}

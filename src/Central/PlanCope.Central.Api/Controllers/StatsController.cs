using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stats")]
public sealed class StatsController(PlanCopeDbContext dbContext, IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet("schools")]
    public async Task<ActionResult> GetSchools(string? schoolYear, string? course, CancellationToken cancellationToken)
    {
        var rosterScope = User.FindFirstValue("roster_scope");
        IReadOnlyCollection<string> cues;
        if (rosterScope == "province")
        {
            cues = await dbContext.ExamRollups.AsNoTracking().Select(r => r.Cue).Distinct().ToListAsync(cancellationToken);
        }
        else if (rosterScope == "school")
        {
            var candidates = User.FindAll("roster_cue").Select(c => c.Value).Distinct().ToList();
            var authorized = new List<string>();
            foreach (var cue in candidates)
            {
                if ((await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
                {
                    authorized.Add(cue);
                }
            }

            cues = authorized;
        }
        else
        {
            return Forbid();
        }

        var rows = new List<object>();
        foreach (var cue in cues)
        {
            var totals = await QueryTotalsAsync(cue, schoolYear, course, cancellationToken);
            rows.Add(new
            {
                cue,
                attemptCount = Render(SuppressibleValue<int>.For(rosterScope!, totals.AttemptCount, totals.AttemptCount)),
                averageScorePercent = Render(SuppressibleValue<double>.For(rosterScope!, totals.AttemptCount, totals.Percent))
            });
        }

        return Ok(rows);
    }

    [HttpGet("school")]
    public async Task<ActionResult> GetSchool(string cue, string? schoolYear, string? course, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            return BadRequest("cue is required.");
        }

        if (!(await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        var rosterScope = User.FindFirstValue("roster_scope")!;
        var totals = await QueryTotalsAsync(cue, schoolYear, course, cancellationToken);
        return Ok(new
        {
            attemptCount = Render(SuppressibleValue<int>.For(rosterScope, totals.AttemptCount, totals.AttemptCount)),
            averageScorePercent = Render(SuppressibleValue<double>.For(rosterScope, totals.AttemptCount, totals.Percent))
        });
    }

    [HttpGet("course")]
    public async Task<ActionResult> GetCourse(string cue, string? schoolYear, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            return BadRequest("cue is required.");
        }

        if (!(await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        var rosterScope = User.FindFirstValue("roster_scope")!;

        var query = dbContext.ExamRollups.AsNoTracking().Where(r => r.Cue == cue);
        if (schoolYear is not null)
        {
            query = query.Where(r => r.SchoolYear == schoolYear);
        }

        var courseRows = await query
            .GroupBy(r => r.Course)
            .Select(groupBy => new
            {
                Course = groupBy.Key,
                AttemptCount = groupBy.Sum(r => r.AttemptCount),
                ScoreSum = groupBy.Sum(r => r.ScoreSum),
                ScoreMaxSum = groupBy.Sum(r => r.ScoreMaxSum)
            })
            .OrderBy(row => row.Course)
            .ToListAsync(cancellationToken);

        var rows = new List<object>();
        foreach (var courseRow in courseRows)
        {
            var percent = courseRow.ScoreMaxSum > 0 ? courseRow.ScoreSum / courseRow.ScoreMaxSum * 100 : 0;
            rows.Add(new
            {
                course = courseRow.Course,
                attemptCount = Render(SuppressibleValue<int>.For(rosterScope, courseRow.AttemptCount, courseRow.AttemptCount)),
                averageScorePercent = Render(SuppressibleValue<double>.For(rosterScope, courseRow.AttemptCount, percent))
            });
        }

        return Ok(rows);
    }

    [HttpGet("exam")]
    public async Task<ActionResult> GetExam(string cue, string? schoolYear, string? course, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            return BadRequest("cue is required.");
        }

        if (!(await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        var rosterScope = User.FindFirstValue("roster_scope")!;

        var query = dbContext.ExamRollups.AsNoTracking().Where(r => r.Cue == cue);
        if (schoolYear is not null)
        {
            query = query.Where(r => r.SchoolYear == schoolYear);
        }

        if (course is not null)
        {
            query = query.Where(r => r.Course == course);
        }

        var examRows = await (
                from rollup in query
                join version in dbContext.ExamVersions.AsNoTracking() on rollup.ExamVersionId equals version.Id
                join exam in dbContext.Exams.AsNoTracking() on version.ExamId equals exam.Id
                group rollup by new { rollup.ExamVersionId, exam.Code, version.VersionNumber }
                into groupBy
                select new
                {
                    ExamVersionId = groupBy.Key.ExamVersionId,
                    ExamCode = groupBy.Key.Code,
                    VersionNumber = groupBy.Key.VersionNumber,
                    AttemptCount = groupBy.Sum(r => r.AttemptCount),
                    ScoreSum = groupBy.Sum(r => r.ScoreSum),
                    ScoreMaxSum = groupBy.Sum(r => r.ScoreMaxSum)
                })
            .OrderBy(row => row.ExamCode)
            .ThenBy(row => row.VersionNumber)
            .ToListAsync(cancellationToken);

        var rows = new List<object>();
        foreach (var examRow in examRows)
        {
            var blockRows = await (
                    from block in dbContext.ExamRollupBlocks.AsNoTracking()
                    join rollup in dbContext.ExamRollups.AsNoTracking() on block.RollupId equals rollup.Id
                    where rollup.Cue == cue
                          && (schoolYear == null || rollup.SchoolYear == schoolYear)
                          && (course == null || rollup.Course == course)
                          && rollup.ExamVersionId == examRow.ExamVersionId
                    group block by block.BlockId
                    into groupBy
                    select new
                    {
                        BlockId = groupBy.Key,
                        CorrectCount = groupBy.Sum(b => b.CorrectCount),
                        PartialCount = groupBy.Sum(b => b.PartialCount),
                        IncorrectCount = groupBy.Sum(b => b.IncorrectCount),
                        BlankCount = groupBy.Sum(b => b.BlankCount),
                        UngradableCount = groupBy.Sum(b => b.UngradableCount)
                    })
                .OrderBy(row => row.BlockId)
                .ToListAsync(cancellationToken);

            var blocks = blockRows
                .Select(block => new
                {
                    blockId = block.BlockId,
                    correctCount = block.CorrectCount,
                    partialCount = block.PartialCount,
                    incorrectCount = block.IncorrectCount,
                    blankCount = block.BlankCount,
                    ungradableCount = block.UngradableCount
                })
                .ToList();

            var attemptCount = examRow.AttemptCount;
            var percent = examRow.ScoreMaxSum > 0 ? examRow.ScoreSum / examRow.ScoreMaxSum * 100 : 0;

            rows.Add(new
            {
                examVersionId = examRow.ExamVersionId,
                examCode = examRow.ExamCode,
                versionNumber = examRow.VersionNumber,
                attemptCount = Render(SuppressibleValue<int>.For(rosterScope, attemptCount, attemptCount)),
                averageScorePercent = Render(SuppressibleValue<double>.For(rosterScope, attemptCount, percent)),
                blocks
            });
        }

        return Ok(rows);
    }

    private async Task<(int AttemptCount, double Percent)> QueryTotalsAsync(string cue, string? schoolYear, string? course, CancellationToken cancellationToken)
    {
        var query = dbContext.ExamRollups.AsNoTracking().Where(r => r.Cue == cue);
        if (schoolYear is not null)
        {
            query = query.Where(r => r.SchoolYear == schoolYear);
        }

        if (course is not null)
        {
            query = query.Where(r => r.Course == course);
        }

        var attemptCount = await query.SumAsync(r => (int?)r.AttemptCount, cancellationToken) ?? 0;
        var scoreSum = await query.SumAsync(r => (double?)r.ScoreSum, cancellationToken) ?? 0;
        var scoreMaxSum = await query.SumAsync(r => (double?)r.ScoreMaxSum, cancellationToken) ?? 0;
        var percent = scoreMaxSum > 0 ? scoreSum / scoreMaxSum * 100 : 0;
        return (attemptCount, percent);
    }

    private static object Render(SuppressibleValue<int> value) => value.IsSuppressed ? SuppressibleValue<int>.SuppressionLabel : value.Value;

    private static object Render(SuppressibleValue<double> value) => value.IsSuppressed ? SuppressibleValue<double>.SuppressionLabel : value.Value;
}

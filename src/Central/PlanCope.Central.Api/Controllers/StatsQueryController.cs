using System.Security.Claims;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stats")]
public sealed partial class StatsQueryController(PlanCopeDbContext db, IAuthorizationService authorization) : ControllerBase
{
    private static readonly string[] Dimensions = ["locality", "department", "course", "subject", "year", "school", "version"];

    [HttpGet("summary")]
    public async Task<ActionResult<StatsSummaryDto>> Summary(CancellationToken ct)
    {
        var scope = User.FindFirstValue("roster_scope");
        if (scope is not ("province" or "school")) return Forbid();
        var cues = await AuthorizedCuesAsync(ct);
        if (cues is null) return Forbid();
        var rollups = db.ExamRollups.AsNoTracking();
        if (scope == "school" || cues.Count > 0) rollups = rollups.Where(r => cues.Contains(r.Cue));
        var totals = await rollups.GroupBy(_ => 1).Select(g => new
        {
            Attempts = g.Sum(r => r.AttemptCount),
            Schools = g.Where(r => r.AttemptCount > 0).Select(r => r.Cue).Distinct().Count(),
            Latest = g.Max(r => (DateTimeOffset?)r.UpdatedAt)
        }).SingleOrDefaultAsync(ct);
        int publishedExamCount;
        if (scope == "province")
        {
            publishedExamCount = await db.ExamVersions.AsNoTracking().Where(v => v.Status == "Published")
                .Select(v => v.ExamId).Distinct().CountAsync(ct);
        }
        else
        {
            var versionIds = await rollups.Select(r => r.ExamVersionId).Distinct().ToListAsync(ct);
            var sessionVersions = db.DeliverySessions.AsNoTracking().Where(s => s.ExamVersionId != null && cues.Contains(s.SchoolId!)).Select(s => s.ExamVersionId!);
            versionIds.AddRange(await sessionVersions.Distinct().ToListAsync(ct));
            versionIds = versionIds.Distinct().ToList();
            publishedExamCount = await db.ExamVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.Id) && v.Status == "Published")
                .Select(v => v.ExamId).Distinct().CountAsync(ct);
        }
        var cutoff = DateTimeOffset.UtcNow - SessionHeartbeatPolicy.StaleAfter;
        var sessions = db.DeliverySessions.AsNoTracking().Where(s =>
            (s.Status == "active" || s.Status == "paused") && s.LastHeartbeatAt >= cutoff && s.SchoolId != null);
        if (scope == "school" || cues.Count > 0) sessions = sessions.Where(s => cues.Contains(s.SchoolId!));
        var fresh = await sessions.CountAsync(ct);
        StatsMetricDto<int>? pending = null;
        if (scope == "province" || cues.Count > 0)
        {
            var pendingQuery = from attempt in db.ReceivedStudentAttempts.AsNoTracking()
                               join session in db.DeliverySessions.AsNoTracking() on attempt.DeliverySessionId equals session.Id
                               where attempt.AttributionStatus == "pending" && session.SchoolId != null
                               select session.SchoolId!;
            if (cues.Count > 0) pendingQuery = pendingQuery.Where(cue => cues.Contains(cue));
            pending = Available(await pendingQuery.CountAsync(ct), scope);
        }
        var latest = totals?.Latest?.ToString("O");
        return Ok(new StatsSummaryDto(
            Available(publishedExamCount, scope),
            Available(totals?.Schools ?? 0, scope),
            Available(totals?.Attempts ?? 0, scope),
            Available(fresh, scope), pending, DateTimeOffset.UtcNow.ToString("O"), latest));
    }

    private async Task<IQueryable<ExamRollup>?> ScopedRollupsAsync(CancellationToken ct)
    {
        var scope = User.FindFirstValue("roster_scope");
        if (scope is not ("province" or "school")) return null;
        var cues = await AuthorizedCuesAsync(ct);
        if (cues is null) return null;
        var query = db.ExamRollups.AsNoTracking();
        return scope == "province" ? query : query.Where(r => cues.Contains(r.Cue));
    }

    private async Task<List<string>?> AuthorizedCuesAsync(CancellationToken ct)
    {
        var scope = User.FindFirstValue("roster_scope");
        if (scope == "province") return [];
        if (scope != "school") return null;
        var candidates = User.FindAll("roster_cue").Select(c => c.Value).Distinct().ToArray();
        var result = new List<string>();
        foreach (var cue in candidates)
            if ((await authorization.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded && CueCode.TryNormalize(cue, out var normalized)) result.Add(normalized);
        return result;
    }

    private static bool ValidatePaging(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;

    private static StatsMetricDto<int> Available(int value, string scope) => scope == "province" && value is > 0 and < CohortSuppression.MinimumCohort ? new(null, "suppressed") : new(value, "available");

    private static StatsMetricDto<double> Available(double value, int count, string scope) => scope == "province" && count is > 0 and < CohortSuppression.MinimumCohort ? new(null, "suppressed") : new(value, "available");

    private static StatsMetricDto<double> Available(double value, string scope) => scope == "province" ? new(null, "suppressed") : new(value, "available");

    private static IQueryable<School> RestrictSchoolsToCues(IQueryable<School> schools, IReadOnlyCollection<string> cues)
    {
        var cueNumbers = cues.Select(cue => long.Parse(cue, CultureInfo.InvariantCulture)).ToList();
        return schools.Where(school =>
            (school.Cue > 9_999_999 && cueNumbers.Contains(school.Cue)) ||
            (school.Cue <= 9_999_999 && cueNumbers.Contains(school.Cue * 100 + (school.Annex ?? 0))));
    }
}

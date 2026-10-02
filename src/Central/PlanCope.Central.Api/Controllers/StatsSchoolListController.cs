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

public sealed partial class StatsQueryController
{
    [HttpGet("school-list")]
    public async Task<ActionResult<StatsPageDto<SchoolStatsListItemDto>>> SchoolList(int page = 1, int pageSize = 50, string? schoolYear = null, string? course = null, CancellationToken ct = default)
    {
        if (!ValidatePaging(page, pageSize) || (schoolYear is not null && (schoolYear.Length != 4 || !schoolYear.All(char.IsAsciiDigit)))) return BadRequest("Invalid pagination or filter.");
        var scope = User.FindFirstValue("roster_scope");
        if (scope is not ("province" or "school")) return Forbid();
        var cues = await AuthorizedCuesAsync(ct);
        if (cues is null) return Forbid();
        var schoolQuery = db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active");
        List<(School School, string Cue)> scoped;
        int totalCount;
        if (scope == "province")
        {
            totalCount = await schoolQuery.CountAsync(ct);
            scoped = (await schoolQuery.OrderBy(s => s.Name).ThenBy(s => s.Cue).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct))
                .Select(s => CueCode.TryFromSchool(s.Cue, s.Annex, out var cue) ? (s, cue) : default)
                .Where(x => x.s is not null).Select(x => (x.s, x.cue)).ToList();
        }
        else
        {
            var authorizedCueNumbers = cues.Select(cue => long.Parse(cue, CultureInfo.InvariantCulture)).ToList();
            schoolQuery = schoolQuery.Where(school =>
                (school.Cue > 9_999_999 && authorizedCueNumbers.Contains(school.Cue)) ||
                (school.Cue <= 9_999_999 && authorizedCueNumbers.Contains(school.Cue * 100 + (school.Annex ?? 0))));
            totalCount = await schoolQuery.CountAsync(ct);
            scoped = (await schoolQuery.OrderBy(s => s.Name).ThenBy(s => s.Cue)
                    .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct))
                .Select(s => CueCode.TryFromSchool(s.Cue, s.Annex, out var cue) ? (s, cue) : default)
                .Where(x => x.s is not null)
                .Select(x => (x.s, x.cue)).ToList();
        }
        var cueList = scoped.Select(x => x.Cue).Distinct().ToList();
        var schoolIds = scoped.Select(x => x.School.Id).Distinct().ToList();
        var geographyRows = await (from school in db.Schools.AsNoTracking().Where(s => schoolIds.Contains(s.Id))
                                   join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
                                   join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
                                   select new { school, locality, department }).ToListAsync(ct);
        var geographyByCue = geographyRows
            .Select(row => CueCode.TryFromSchool(row.school.Cue, row.school.Annex, out var cue)
                ? new SchoolDimension(cue, row.school.Name, row.school.Annex, row.school.LocalityId, row.locality.Name, row.department.Id, row.department.Name)
                : null)
            .OfType<SchoolDimension>()
            .ToDictionary(item => item.Cue, StringComparer.Ordinal);
        var rollupQuery = db.ExamRollups.AsNoTracking().Where(r => cueList.Contains(r.Cue));
        if (schoolYear is not null) rollupQuery = rollupQuery.Where(r => r.SchoolYear == schoolYear);
        if (course is not null) rollupQuery = rollupQuery.Where(r => r.Course == course);
        var totals = await rollupQuery.GroupBy(r => r.Cue).Select(g => new { Cue = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum), Latest = g.Max(r => (DateTimeOffset?)r.UpdatedAt) }).ToDictionaryAsync(r => r.Cue, ct);
        var cutoff = DateTimeOffset.UtcNow - SessionHeartbeatPolicy.StaleAfter;
        var liveRows = await db.DeliverySessions.AsNoTracking().Where(s => cueList.Contains(s.SchoolId!) && (s.Status == "active" || s.Status == "paused") && s.LastHeartbeatAt >= cutoff).Select(s => s.SchoolId!).Distinct().ToListAsync(ct);
        var live = liveRows.ToHashSet(StringComparer.Ordinal);
        var rows = scoped.Select(x => {
            totals.TryGetValue(x.Cue, out var total);
            var geography = geographyByCue.GetValueOrDefault(x.Cue);
            return new SchoolStatsListItemDto(x.Cue, x.School.Name,
                Available(total?.Attempts ?? 0, scope!),
                total?.Max > 0 ? Available(total.Score / total.Max * 100, total.Attempts, scope!) : new StatsMetricDto<double>(null, "unavailable"),
                live.Contains(x.Cue), total?.Latest?.ToString("O"), x.School.Annex,
                geography?.LocalityId, geography?.Locality, geography?.DepartmentId, geography?.Department);
        }).ToArray();
        return Ok(new StatsPageDto<SchoolStatsListItemDto>(page, pageSize, totalCount, rows));
    }
}

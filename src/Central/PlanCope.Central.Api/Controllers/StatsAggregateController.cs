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
    [HttpGet("aggregate")]
    public async Task<ActionResult<StatsAggregateDto>> Aggregate(
        string groupBy, string? localityId = null, string? departmentId = null, string? course = null,
        string? subject = null, string? schoolYear = null, string? school = null, string? version = null,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (!Dimensions.Contains(groupBy, StringComparer.Ordinal) || !ValidatePaging(page, pageSize)) return BadRequest("Invalid dimension or pagination.");
        if (new[] { course, subject, schoolYear, school, version }.Any(v => v is not null && (string.IsNullOrWhiteSpace(v) || v.Length > 120)) ||
            new[] { localityId, departmentId }.Any(v => v is not null && (string.IsNullOrWhiteSpace(v) || v.Length > 80))) return BadRequest("Invalid filter.");
        if (schoolYear is not null && (schoolYear.Length != 4 || !schoolYear.All(char.IsAsciiDigit))) return BadRequest("Invalid schoolYear.");
        if (school is not null && !CueCode.TryNormalize(school, out _)) return BadRequest("Invalid school CUE.");
        if (school is not null && !(await authorization.AuthorizeAsync(User, school, new RosterScopeRequirement())).Succeeded) return Forbid();
        var query = await ScopedRollupsAsync(ct);
        if (query is null) return Forbid();
        var authorizedCues = await AuthorizedCuesAsync(ct);
        if (authorizedCues is null) return Forbid();
        if (localityId is not null && !await GeographyFilterExistsAsync(localityId, null, authorizedCues, ct)) return BadRequest("Unknown localityId.");
        if (departmentId is not null && !await GeographyFilterExistsAsync(null, departmentId, authorizedCues, ct)) return BadRequest("Unknown departmentId.");
        if (course is not null && !await query.AnyAsync(r => r.Course == course, ct)) return BadRequest("Unknown course.");
        if (schoolYear is not null && !await query.AnyAsync(r => r.SchoolYear == schoolYear, ct)) return BadRequest("Unknown schoolYear.");
        if (version is not null && !await query.AnyAsync(r => r.ExamVersionId == version, ct)) return BadRequest("Unknown version.");
        if (school is not null && !await AuthorizedSchoolExistsAsync(CueCode.Normalize(school), authorizedCues, ct)) return BadRequest("Unknown school.");
        if (localityId is not null || departmentId is not null)
            query = FilterRollupsByGeography(query, localityId, departmentId);
        if (course is not null) query = query.Where(r => r.Course == course);
        if (schoolYear is not null) query = query.Where(r => r.SchoolYear == schoolYear);
        if (school is not null) query = query.Where(r => r.Cue == CueCode.Normalize(school));
        if (version is not null) query = query.Where(r => r.ExamVersionId == version);
        var needsSubjects = subject is not null || groupBy == "subject";
        var versionIds = needsSubjects ? await query.Select(r => r.ExamVersionId).Distinct().ToArrayAsync(ct) : [];
        var subjects = needsSubjects ? await SubjectsAsync(versionIds, ct) : new Dictionary<string, string>(StringComparer.Ordinal);
        if (subject is not null && !subjects.Values.Contains(subject, StringComparer.Ordinal) && subject != "sin_asignar") return BadRequest("Unknown subject.");
        if (subject is not null)
        {
            var matchingVersionIds = subjects.Where(pair => string.Equals(subject, pair.Value, StringComparison.Ordinal)).Select(pair => pair.Key).ToList();
            query = query.Where(r => matchingVersionIds.Contains(r.ExamVersionId));
        }

        var latestRollupUpdatedAt = await query.MaxAsync(r => (DateTimeOffset?)r.UpdatedAt, ct);
        List<AggregateRollupRow> grouped;
        int totalCount;
        if (groupBy == "course")
        {
            totalCount = await query.Select(r => r.Course).Distinct().CountAsync(ct);
            var pageRows = await query.GroupBy(r => r.Course).Select(g => new
                { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum), Latest = g.Max(r => r.UpdatedAt) })
                .OrderBy(r => r.Label).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            grouped = pageRows.Select(r => new AggregateRollupRow(r.Key, r.Label, r.Attempts, r.Score, r.Max, r.Latest)).ToList();
        }
        else if (groupBy == "year")
        {
            totalCount = await query.Select(r => r.SchoolYear).Distinct().CountAsync(ct);
            var pageRows = await query.GroupBy(r => r.SchoolYear).Select(g => new
                { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum), Latest = g.Max(r => r.UpdatedAt) })
                .OrderBy(r => r.Label).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            grouped = pageRows.Select(r => new AggregateRollupRow(r.Key, r.Label, r.Attempts, r.Score, r.Max, r.Latest)).ToList();
        }
        else if (groupBy == "version")
        {
            totalCount = await query.Select(r => r.ExamVersionId).Distinct().CountAsync(ct);
            var pageRows = await query.GroupBy(r => r.ExamVersionId).Select(g => new
                { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum), Latest = g.Max(r => r.UpdatedAt) })
                .OrderBy(r => r.Label).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var versionLabels = await VersionLabelsAsync(pageRows.Select(row => row.Key).ToArray(), ct);
            grouped = pageRows.Select(r => new AggregateRollupRow(r.Key, versionLabels.GetValueOrDefault(r.Key, r.Key), r.Attempts, r.Score, r.Max, r.Latest)).ToList();
        }
        else if (groupBy == "school")
        {
            totalCount = await query.Select(r => r.Cue).Distinct().CountAsync(ct);
            var pageRows = await query.GroupBy(r => r.Cue).Select(g => new
                { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum), Latest = g.Max(r => r.UpdatedAt) })
                .OrderBy(r => r.Key).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            grouped = pageRows.Select(r => new AggregateRollupRow(r.Key, r.Label, r.Attempts, r.Score, r.Max, r.Latest)).ToList();
            var schoolMap = await SchoolDimensionRowsAsync(grouped.Select(row => row.Key).ToArray(), ct);
            grouped = grouped.Select(row => schoolMap.TryGetValue(row.Key, out var school)
                ? row with { Label = school.Name }
                : row).OrderBy(row => row.Key, StringComparer.Ordinal).ToList();
        }
        else if (groupBy is "locality" or "department")
        {
            var localityDimension = groupBy == "locality";
            var dimensionRows = from rollup in query
                                join schoolRow in db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active")
                                    on Convert.ToInt64(rollup.Cue) equals (schoolRow.Cue > 9_999_999 ? schoolRow.Cue : schoolRow.Cue * 100 + (schoolRow.Annex ?? 0)) into schoolGroups
                                from schoolRow in schoolGroups.DefaultIfEmpty()
                                join localityRow in db.Localities.AsNoTracking() on schoolRow.LocalityId equals localityRow.Id into localityGroups
                                from localityRow in localityGroups.DefaultIfEmpty()
                                join departmentRow in db.Departments.AsNoTracking() on localityRow.DepartmentId equals departmentRow.Id into departmentGroups
                                from departmentRow in departmentGroups.DefaultIfEmpty()
                                select new
                                {
                                    Key = localityDimension ? localityRow == null ? "sin_asignar" : localityRow.Id : departmentRow == null ? "sin_asignar" : departmentRow.Id,
                                    Label = localityDimension ? localityRow == null ? "sin_asignar" : localityRow.Name : departmentRow == null ? "sin_asignar" : departmentRow.Name,
                                    rollup.AttemptCount,
                                    rollup.ScoreSum,
                                    rollup.ScoreMaxSum,
                                    rollup.UpdatedAt
                                };
            var groupedQuery = dimensionRows.GroupBy(row => new { row.Key, row.Label }).Select(group => new
            {
                group.Key.Key,
                group.Key.Label,
                Attempts = group.Sum(row => row.AttemptCount),
                Score = group.Sum(row => row.ScoreSum),
                Max = group.Sum(row => row.ScoreMaxSum),
                Latest = group.Max(row => row.UpdatedAt)
            });
            totalCount = await groupedQuery.CountAsync(ct);
            var pageRows = await groupedQuery.OrderBy(row => row.Label).ThenBy(row => row.Key)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            grouped = pageRows.Select(row => new AggregateRollupRow(row.Key, row.Label, row.Attempts, row.Score, row.Max, row.Latest)).ToList();
        }
        else if (groupBy == "subject")
        {
            var subjectSelector = SubjectSelector(subjects);
            totalCount = await query.Select(subjectSelector).Distinct().CountAsync(ct);
            var pageRows = await query.GroupBy(subjectSelector).Select(group => new
                    { Key = group.Key, Label = group.Key, Attempts = group.Sum(row => row.AttemptCount), Score = group.Sum(row => row.ScoreSum), Max = group.Sum(row => row.ScoreMaxSum), Latest = group.Max(row => row.UpdatedAt) })
                .OrderBy(row => row.Label).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            grouped = pageRows.Select(row => new AggregateRollupRow(row.Key, row.Label, row.Attempts, row.Score, row.Max, row.Latest)).ToList();
        }
        else
        {
            return BadRequest("Invalid dimension.");
        }
        var scope = User.FindFirstValue("roster_scope")!;
        var rows = grouped.Select(g => new StatsAggregateRowDto(
            g.Key, g.Label, Available(g.Attempts, scope), g.Max > 0 ? Available(g.Score / g.Max * 100, g.Attempts, scope) : new StatsMetricDto<double>(null, "unavailable"))).ToArray();
        var filters = new Dictionary<string, string?> { ["localityId"] = localityId, ["departmentId"] = departmentId, ["course"] = course, ["subject"] = subject, ["schoolYear"] = schoolYear, ["school"] = school, ["version"] = version };
        return Ok(new StatsAggregateDto(totalCount == 0 ? "no_results" : "available", DateTimeOffset.UtcNow.ToString("O"), latestRollupUpdatedAt?.ToString("O"), groupBy, filters, rows, page, pageSize, totalCount));
    }

    private async Task<Dictionary<string, SchoolDimension>> SchoolDimensionRowsAsync(string[]? requested, CancellationToken ct)
    {
        var schoolQuery = db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active");
        if (requested is not null)
        {
            var cueNumbers = requested.Where(cue => CueCode.TryNormalize(cue, out _)).Select(cue => long.Parse(cue, CultureInfo.InvariantCulture)).ToList();
            schoolQuery = schoolQuery.Where(school =>
                (school.Cue > 9_999_999 && cueNumbers.Contains(school.Cue)) ||
                (school.Cue <= 9_999_999 && cueNumbers.Contains(school.Cue * 100 + (school.Annex ?? 0))));
        }
        var query = from school in schoolQuery
                    join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
                    join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
                    select new { school, locality, department };
        var rows = await query.ToListAsync(ct);
        var result = new Dictionary<string, SchoolDimension>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!CueCode.TryFromSchool(row.school.Cue, row.school.Annex, out var cue) || (requested is not null && !requested.Contains(cue, StringComparer.Ordinal))) continue;
            result.TryAdd(cue, new SchoolDimension(cue, row.school.Name, row.school.Annex, row.school.LocalityId, row.locality.Name, row.department.Id, row.department.Name));
        }
        return result;
    }

    private async Task<bool> GeographyFilterExistsAsync(string? localityId, string? departmentId, IReadOnlyCollection<string> authorizedCues, CancellationToken ct)
    {
        var schools = db.Schools.AsNoTracking().Where(school => school.DeletedAt == null && school.Status == "Active");
        if (User.FindFirstValue("roster_scope") == "school") schools = RestrictSchoolsToCues(schools, authorizedCues);
        var query = from school in schools
                    join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
                    join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
                    where (localityId == null || locality.Id == localityId) && (departmentId == null || department.Id == departmentId)
                    select school.Id;
        return await query.AnyAsync(ct);
    }

    private async Task<bool> AuthorizedSchoolExistsAsync(string cue, IReadOnlyCollection<string> authorizedCues, CancellationToken ct)
    {
        var cueNumber = long.Parse(cue, CultureInfo.InvariantCulture);
        var schools = db.Schools.AsNoTracking().Where(school => school.DeletedAt == null && school.Status == "Active");
        if (User.FindFirstValue("roster_scope") == "school") schools = RestrictSchoolsToCues(schools, authorizedCues);
        return await schools.AnyAsync(school =>
            (school.Cue > 9_999_999 && school.Cue == cueNumber) ||
            (school.Cue <= 9_999_999 && school.Cue * 100 + (school.Annex ?? 0) == cueNumber), ct);
    }

    private IQueryable<ExamRollup> FilterRollupsByGeography(IQueryable<ExamRollup> rollups, string? localityId, string? departmentId)
    {
        return from rollup in rollups
               join school in db.Schools.AsNoTracking().Where(row => row.DeletedAt == null && row.Status == "Active")
                   on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0))
               join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
               join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
               where (localityId == null || locality.Id == localityId) && (departmentId == null || department.Id == departmentId)
               select rollup;
    }

    private async Task<Dictionary<string, string>> VersionLabelsAsync(string[] versionIds, CancellationToken ct)
    {
        if (versionIds.Length == 0) return new(StringComparer.Ordinal);
        var ids = versionIds.ToList();
        return await (from version in db.ExamVersions.AsNoTracking()
                      join exam in db.Exams.AsNoTracking() on version.ExamId equals exam.Id
                      where ids.Contains(version.Id)
                      select new { version.Id, Label = exam.Title + " · Versión " + version.VersionNumber })
            .ToDictionaryAsync(row => row.Id, row => row.Label, StringComparer.Ordinal, ct);
    }

    private sealed record AggregateRollupRow(string Key, string Label, int Attempts, double Score, double Max, DateTimeOffset UpdatedAt);
    private sealed record SchoolDimension(string Cue, string Name, int? Annex, string LocalityId, string Locality, string DepartmentId, string Department);
}

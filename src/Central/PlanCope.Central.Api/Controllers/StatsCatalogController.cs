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
    [HttpGet("catalogs")]
    public async Task<ActionResult<StatsPageDto<StatsOptionDto>>> Catalogs(
        string dimension, string? query = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (!ValidatePaging(page, pageSize) || !Dimensions.Contains(dimension, StringComparer.Ordinal)) return BadRequest("Invalid dimension or pagination.");
        if (query?.Length > 120) return BadRequest("query is too long.");
        var rollups = await ScopedRollupsAsync(ct);
        if (rollups is null) return Forbid();
        var loweredQuery = query?.ToLowerInvariant();
        if (dimension is "course" or "year")
        {
            var values = dimension == "course" ? rollups.Select(row => row.Course) : rollups.Select(row => row.SchoolYear);
            return Ok(await StringCatalogAsync(values.Distinct(), loweredQuery, page, pageSize, ct));
        }
        if (dimension == "subject")
        {
            var versionIds = await rollups.Select(row => row.ExamVersionId).Distinct().ToArrayAsync(ct);
            var subjects = await SubjectsAsync(versionIds, ct);
            var subjectSelector = SubjectSelector(subjects);
            return Ok(await StringCatalogAsync(rollups.Select(subjectSelector).Distinct(), loweredQuery, page, pageSize, ct));
        }
        if (dimension == "version")
        {
            var versionIds = rollups.Select(row => row.ExamVersionId);
            var versions = from version in db.ExamVersions.AsNoTracking()
                           join exam in db.Exams.AsNoTracking() on version.ExamId equals exam.Id
                           where versionIds.Contains(version.Id)
                           select new { Value = version.Id, Label = exam.Title + " · Versión " + version.VersionNumber };
            if (loweredQuery is not null)
                versions = versions.Where(option => option.Value.ToLower().Contains(loweredQuery) || option.Label.ToLower().Contains(loweredQuery));
            var total = await versions.CountAsync(ct);
            var items = await versions.OrderBy(option => option.Label).ThenBy(option => option.Value)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(option => new { option.Value, option.Label }).ToListAsync(ct);
            return Ok(new StatsPageDto<StatsOptionDto>(page, pageSize, total,
                items.Select(option => new StatsOptionDto(option.Value, option.Label)).ToArray()));
        }
        if (dimension is "locality" or "department")
        {
            var localityDimension = dimension == "locality";
            var values = from rollup in rollups
                         join school in db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active")
                             on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0))
                         join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
                         join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
                         select new
                         {
                             Value = localityDimension ? locality.Id : department.Id,
                             Label = localityDimension ? locality.Name : department.Name
                         };
            var options = values.GroupBy(option => new { option.Value, option.Label }).Select(group => new { group.Key.Value, group.Key.Label });
            if (loweredQuery is not null)
                options = options.Where(option => option.Value.ToLower().Contains(loweredQuery) || option.Label.ToLower().Contains(loweredQuery));
            var total = await options.CountAsync(ct);
            var items = await options.OrderBy(option => option.Label).ThenBy(option => option.Value)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Ok(new StatsPageDto<StatsOptionDto>(page, pageSize, total,
                items.Select(option => new StatsOptionDto(option.Value, option.Label)).ToArray()));
        }
        if (dimension == "school")
        {
            var schools = db.Schools.AsNoTracking().Where(school => school.DeletedAt == null && school.Status == "Active");
            if (User.FindFirstValue("roster_scope") == "school")
            {
                var authorizedCues = await AuthorizedCuesAsync(ct);
                if (authorizedCues is null) return Forbid();
                schools = RestrictSchoolsToCues(schools, authorizedCues);
            }
            if (query is not null)
            {
                if (CueCode.TryNormalize(query, out var normalizedCue))
                {
                    var cueNumber = long.Parse(normalizedCue, CultureInfo.InvariantCulture);
                    schools = schools.Where(school => (school.Cue > 9_999_999 && school.Cue == cueNumber) ||
                        (school.Cue <= 9_999_999 && school.Cue * 100 + (school.Annex ?? 0) == cueNumber));
                }
                else
                {
                    var searchDigits = new string(query.Where(char.IsAsciiDigit).ToArray());
                    var searchText = query.ToLowerInvariant();
                    schools = schools.Where(school => school.Name.ToLower().Contains(searchText) ||
                        (searchDigits.Length > 0 && (school.Cue.ToString().Contains(searchDigits) || (school.Annex ?? 0).ToString().Contains(searchDigits))));
                }
            }
            var total = await schools.CountAsync(ct);
            var schoolPage = await schools.OrderBy(school => school.Name).ThenBy(school => school.Cue)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(school => new { school.Cue, school.Annex, school.Name }).ToListAsync(ct);
            var items = schoolPage.Select(school => CueCode.TryFromSchool(school.Cue, school.Annex, out var cue)
                ? new StatsOptionDto(cue, school.Name)
                : null).OfType<StatsOptionDto>().ToArray();
            return Ok(new StatsPageDto<StatsOptionDto>(page, pageSize, total, items));
        }
        return BadRequest("Invalid dimension.");
    }

    private async Task<Dictionary<string, string>> SubjectsAsync(string[] versionIds, CancellationToken ct)
    {
        if (versionIds.Length == 0) return new(StringComparer.Ordinal);
        var versionIdList = versionIds.ToList();
        var versions = await db.ExamVersions.AsNoTracking().Where(v => versionIdList.Contains(v.Id)).Select(v => new { v.Id, v.Metadata }).ToListAsync(ct);
        var result = versions.ToDictionary(v => v.Id, v => ReadSubject(v.Metadata), StringComparer.Ordinal);
        var missing = result.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray();
        if (missing.Length > 0)
        {
            var missingList = missing.ToList();
            var packages = await db.PublicationPackages.AsNoTracking().Where(p => missingList.Contains(p.ExamVersionId) && p.Status == "Published").OrderByDescending(p => p.PackageVersion).Select(p => new { p.ExamVersionId, p.Manifest }).ToListAsync(ct);
            var packageSubjects = packages.Select(p => (p.ExamVersionId, Subject: ReadPublishedSubject(p.Manifest))).Where(p => p.Subject is not null).GroupBy(p => p.ExamVersionId).ToDictionary(g => g.Key, g => g.First().Subject!, StringComparer.Ordinal);
            var targetIds = packages.Select(p => p.ExamVersionId).ToList();
            var targets = await (from target in db.PublicationTargets.AsNoTracking()
                                 join package in db.PublicationPackages.AsNoTracking() on target.PublicationPackageId equals package.Id
                                 where targetIds.Contains(package.ExamVersionId) && package.Status == "Published" && target.TargetType == "subject"
                                 select new { package.ExamVersionId, target.TargetId, package.PackageVersion }).OrderByDescending(t => t.PackageVersion).ToListAsync(ct);
            foreach (var group in targets.Where(t => !string.IsNullOrWhiteSpace(t.TargetId)).GroupBy(t => t.ExamVersionId)) packageSubjects.TryAdd(group.Key, group.First().TargetId!);
            foreach (var key in missing) if (packageSubjects.TryGetValue(key, out var value)) result[key] = value;
        }
        return result.ToDictionary(pair => pair.Key, pair => pair.Value ?? "sin_asignar", StringComparer.Ordinal);
    }

    private static string? ReadSubject(JsonDocument? metadata) => metadata is not null && metadata.RootElement.ValueKind == JsonValueKind.Object && metadata.RootElement.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.String ? NormalizeSubject(subject.GetString()) : null;
    private static string? ReadPublishedSubject(JsonDocument manifest)
    {
        if (manifest.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (manifest.RootElement.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.String) return NormalizeSubject(subject.GetString());
        if (manifest.RootElement.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            foreach (var target in targets.EnumerateArray()) if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty("targetType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "subject" && target.TryGetProperty("targetId", out var value) && value.ValueKind == JsonValueKind.String) return NormalizeSubject(value.GetString());
        return null;
    }

    private static string? NormalizeSubject(string? subject) => string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();

    private async Task<StatsPageDto<StatsOptionDto>> StringCatalogAsync(IQueryable<string> values, string? loweredQuery, int page, int pageSize, CancellationToken ct)
    {
        if (loweredQuery is not null) values = values.Where(value => value.ToLower().Contains(loweredQuery));
        var total = await values.CountAsync(ct);
        var pageValues = await values.OrderBy(value => value).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new StatsPageDto<StatsOptionDto>(page, pageSize, total, pageValues.Select(value => new StatsOptionDto(value, value)).ToArray());
    }

    private static Expression<Func<ExamRollup, string>> SubjectSelector(Dictionary<string, string> subjects)
    {
        var rollup = Expression.Parameter(typeof(ExamRollup), "rollup");
        var versionId = Expression.Property(rollup, nameof(ExamRollup.ExamVersionId));
        Expression selector = Expression.Constant("sin_asignar");
        var containsMethod = typeof(List<string>).GetMethod(nameof(List<string>.Contains), [typeof(string)])!;
        foreach (var group in subjects.GroupBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var ids = group.Select(pair => pair.Key).ToList();
            var contains = Expression.Call(Expression.Constant(ids), containsMethod, versionId);
            selector = Expression.Condition(contains, Expression.Constant(group.Key), selector);
        }
        return Expression.Lambda<Func<ExamRollup, string>>(selector, rollup);
    }
}

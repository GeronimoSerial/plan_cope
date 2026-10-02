using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Services;

public sealed class StatsShareService(PlanCopeDbContext db)
{
    private static readonly HashSet<string> GroupDimensions = new(["locality", "department", "course", "subject", "year"], StringComparer.Ordinal);
    private static readonly HashSet<string> FilterDimensions = new(["departmentId", "localityId", "course", "subject", "schoolYear"], StringComparer.Ordinal);
    private static readonly HashSet<string> PublicMetrics = new(["attemptCount", "weightedScorePercent"], StringComparer.Ordinal);
    private const int MinimumCohort = 5;
    private const int MaximumGroups = 5000;

    public async Task<(StatsShareCreatedDto? Created, string? Error)> CreateAsync(
        StatsShareCreateRequest request,
        string createdBy,
        IReadOnlyCollection<string>? authorizedCues,
        string webBaseUrl,
        string? ip,
        CancellationToken ct)
    {
        if (!GroupDimensions.Contains(request.GroupBy)) return (null, "La agrupación solicitada no está permitida.");
        if (request.Metrics is not null && (request.Metrics.Count != PublicMetrics.Count || request.Metrics.Distinct(StringComparer.Ordinal).Count() != PublicMetrics.Count || request.Metrics.Any(metric => !PublicMetrics.Contains(metric)))) return (null, "Las métricas solicitadas no están permitidas.");
        if (request.Filters is null || request.Filters.Keys.Any(key => !FilterDimensions.Contains(key))) return (null, "Hay filtros no permitidos.");
        var filters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, raw) in request.Filters)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            if (value.Length > 120 || value.Any(char.IsControl)) return (null, "Un filtro tiene un formato inválido.");
            filters[key] = value;
        }

        var now = DateTimeOffset.UtcNow;
        var expires = request.ExpiresAt?.ToUniversalTime() ?? now.AddDays(7);
        if (expires <= now || expires > now.AddDays(30)) return (null, "La vigencia debe ser futura y no superar 30 días.");

        List<long>? cueNumbers = null;
        if (authorizedCues is not null)
        {
            if (authorizedCues.Count == 0) return (null, "No hay alcance autorizado para compartir.");
            cueNumbers = authorizedCues.Select(cue => long.TryParse(cue, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (long?)null)
                .Where(value => value is not null).Select(value => value!.Value).Distinct().ToList();
            if (cueNumbers.Count == 0) return (null, "No hay alcance autorizado para compartir.");
        }
        filters.TryGetValue("schoolYear", out var year);
        filters.TryGetValue("course", out var course);
        filters.TryGetValue("localityId", out var localityId);
        filters.TryGetValue("departmentId", out var departmentId);
        var rollupQuery = BuildFilteredRollups(cueNumbers, year, course, localityId, departmentId);

        var subjectValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.GroupBy == "subject" || filters.ContainsKey("subject"))
        {
            var versionIds = await rollupQuery.Select(r => r.ExamVersionId).Distinct().ToListAsync(ct);
            subjectValues = await ResolveSubjectsAsync(versionIds, ct);
            if (filters.TryGetValue("subject", out var selectedSubject))
            {
                var selectedVersionIds = subjectValues.Where(pair => pair.Value == selectedSubject).Select(pair => pair.Key).ToList();
                if (selectedVersionIds.Count == 0) return (null, "El filtro queda fuera del alcance autorizado.");
                rollupQuery = rollupQuery.Where(r => selectedVersionIds.Contains(r.ExamVersionId));
            }
        }

        if (filters.Count > 0 && !await rollupQuery.AnyAsync(ct)) return (null, "El filtro queda fuera del alcance autorizado.");
        var grouped = await BuildGroupedQuery(request.GroupBy, rollupQuery, subjectValues)
            .OrderBy(g => g.Label).ThenBy(g => g.Key).Take(MaximumGroups + 1).ToListAsync(ct);
        if (grouped.Count > MaximumGroups) return (null, $"El resultado supera el límite explícito de {MaximumGroups} grupos; agregá filtros.");
        var hasHiddenGroups = grouped.Any(g => g.Attempts is > 0 and < MinimumCohort);
        var rows = grouped.Where(g => g.Attempts >= MinimumCohort)
            .Select(g => new PublicStatsShareRowDto(g.Label,
                Metric(g.Attempts, false),
                Metric(g.Max > 0 ? g.Score / g.Max * 100d : null, false)))
            .ToList();
        if (hasHiddenGroups)
        {
            rows.Add(new PublicStatsShareRowDto("Datos suprimidos por privacidad",
                Metric(null, true), Metric(null, true)));
        }
        var totalAttempts = grouped.Sum(g => g.Attempts);
        var totalScore = grouped.Sum(g => g.Score);
        var totalMax = grouped.Sum(g => g.Max);
        var generatedAt = now.ToString("O", CultureInfo.InvariantCulture);
        var groupLabel = request.GroupBy switch { "locality" => "Localidad", "department" => "Departamento", "course" => "Curso", "subject" => "Materia", _ => "Año lectivo" };
        var visibleFilters = await BuildFilterLabelsAsync(filters, ct);
        var snapshot = new PublicStatsShareDto(request.GroupBy, groupLabel, visibleFilters, rows,
            Metric(totalAttempts, hasHiddenGroups || totalAttempts is > 0 and < MinimumCohort),
            Metric(totalMax > 0 ? totalScore / totalMax * 100d : null, hasHiddenGroups || totalAttempts is > 0 and < MinimumCohort),
            generatedAt, expires.ToString("O", CultureInfo.InvariantCulture));

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var id = Guid.NewGuid().ToString("N");
        using var filtersJson = JsonDocument.Parse(JsonSerializer.Serialize(filters));
        using var snapshotJson = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        db.StatsShares.Add(new StatsShare(id, tokenHash, request.GroupBy,
            JsonDocument.Parse(filtersJson.RootElement.GetRawText()),
            JsonDocument.Parse(snapshotJson.RootElement.GetRawText()),
            now, expires, null, createdBy));
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid().ToString("N"), createdBy, "StatsShare", id,
            "stats.share.create", JsonDocument.Parse(JsonSerializer.Serialize(new { groupBy = request.GroupBy, expiresAt = expires })), ip, now));
        await db.SaveChangesAsync(ct);
        return (new StatsShareCreatedDto(id, $"{webBaseUrl.TrimEnd('/')}/estadisticas/compartidas/{token}", expires.ToString("O", CultureInfo.InvariantCulture)), null);
    }

    public async Task<bool> RevokeAsync(string id, string actorId, string? ip, bool unboundedScope, CancellationToken ct)
    {
        var share = await db.StatsShares.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (share is null || share.RevokedAt is not null || (!unboundedScope && share.CreatedBy != actorId)) return false;
        var now = DateTimeOffset.UtcNow;
        db.Entry(share).Property(x => x.RevokedAt).CurrentValue = now;
        db.Entry(share).Property(x => x.RevokedAt).IsModified = true;
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid().ToString("N"), actorId, "StatsShare", share.Id,
            "stats.share.revoke", JsonDocument.Parse("{}"), ip, now));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PublicStatsShareDto?> FindPublicAsync(string token, CancellationToken ct)
    {
        if (token.Length != 43 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var share = await db.StatsShares.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (share is null || share.RevokedAt is not null || share.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        return share.Snapshot.RootElement.Deserialize<PublicStatsShareDto>();
    }

    private async Task<Dictionary<string, string>> ResolveSubjectsAsync(List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new(StringComparer.Ordinal);
        var versionIds = ids.ToList();
        var versions = await db.ExamVersions.AsNoTracking().Where(x => versionIds.Contains(x.Id)).ToListAsync(ct);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var version in versions)
        {
            var subject = version.Metadata?.RootElement.ValueKind == JsonValueKind.Object && version.Metadata.RootElement.TryGetProperty("subject", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(subject)) result[version.Id] = subject;
        }
        var missing = ids.Where(id => !result.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var packages = await db.PublicationPackages.AsNoTracking().Where(x => missing.Contains(x.ExamVersionId) && x.Status == "Published").OrderByDescending(x => x.PackageVersion).ToListAsync(ct);
            foreach (var package in packages)
            {
                if (package.Manifest.RootElement.ValueKind == JsonValueKind.Object && package.Manifest.RootElement.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("subject", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) result.TryAdd(package.ExamVersionId, value.GetString()!.Trim());
            }
            var stillMissing = missing.Where(id => !result.ContainsKey(id)).ToList();
            if (stillMissing.Count > 0)
            {
                var targets = await (from package in db.PublicationPackages.AsNoTracking()
                                     join target in db.PublicationTargets.AsNoTracking() on package.Id equals target.PublicationPackageId
                                     where stillMissing.Contains(package.ExamVersionId) && package.Status == "Published" && target.TargetType == "subject"
                                     orderby package.PackageVersion descending
                                     select new { package.ExamVersionId, target.TargetId }).ToListAsync(ct);
                foreach (var target in targets.Where(x => !string.IsNullOrWhiteSpace(x.TargetId))) result.TryAdd(target.ExamVersionId, target.TargetId!.Trim());
            }
        }
        foreach (var id in ids) result.TryAdd(id, "sin_asignar");
        return result;
    }

    internal IQueryable<GroupAggregate> BuildGroupedQuery(string groupBy, IQueryable<ExamRollup> rollups, IReadOnlyDictionary<string, string> subjects)
    {
        if (groupBy is "course" or "year")
        {
            return groupBy == "course"
                ? rollups.GroupBy(r => r.Course).Select(g => new GroupAggregate { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum) })
                : rollups.GroupBy(r => r.SchoolYear).Select(g => new GroupAggregate { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum) });
        }
        if (groupBy == "subject")
        {
            var selector = SubjectSelector(subjects);
            return rollups.GroupBy(selector).Select(g => new GroupAggregate { Key = g.Key, Label = g.Key, Attempts = g.Sum(r => r.AttemptCount), Score = g.Sum(r => r.ScoreSum), Max = g.Sum(r => r.ScoreMaxSum) });
        }

        var activeSchools = db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active");
        var geography = from rollup in rollups
                        join school in activeSchools on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0)) into schoolRows
                        from school in schoolRows.DefaultIfEmpty()
                        join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id into localityRows
                        from locality in localityRows.DefaultIfEmpty()
                        join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id into departmentRows
                        from department in departmentRows.DefaultIfEmpty()
                        select new { rollup, locality, department };
        return groupBy == "locality"
            ? geography.GroupBy(x => new { Key = x.locality == null ? null : x.locality.Id, Label = x.locality == null ? null : x.locality.Name })
                .Select(g => new GroupAggregate { Key = g.Key.Key ?? "sin_asignar", Label = g.Key.Label ?? "sin_asignar", Attempts = g.Sum(x => x.rollup.AttemptCount), Score = g.Sum(x => x.rollup.ScoreSum), Max = g.Sum(x => x.rollup.ScoreMaxSum) })
            : geography.GroupBy(x => new { Key = x.department == null ? null : x.department.Id, Label = x.department == null ? null : x.department.Name })
                .Select(g => new GroupAggregate { Key = g.Key.Key ?? "sin_asignar", Label = g.Key.Label ?? "sin_asignar", Attempts = g.Sum(x => x.rollup.AttemptCount), Score = g.Sum(x => x.rollup.ScoreSum), Max = g.Sum(x => x.rollup.ScoreMaxSum) });
    }

    internal IQueryable<ExamRollup> BuildFilteredRollups(IReadOnlyCollection<long>? cueNumbers, string? year, string? course, string? localityId, string? departmentId)
    {
        IQueryable<ExamRollup> rollups = db.ExamRollups.AsNoTracking();
        if (cueNumbers is not null) rollups = rollups.Where(r => cueNumbers.Contains(Convert.ToInt64(r.Cue)));
        if (year is not null) rollups = rollups.Where(r => r.SchoolYear == year);
        if (course is not null) rollups = rollups.Where(r => r.Course == course);
        if (localityId is not null || departmentId is not null)
        {
            var activeSchools = db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active");
            var geographyCues = from school in activeSchools
                                 join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
                                 join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
                                 where (localityId == null || locality.Id == localityId) && (departmentId == null || department.Id == departmentId)
                                 select school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0);
            rollups = rollups.Where(r => geographyCues.Contains(Convert.ToInt64(r.Cue)));
        }
        return rollups;
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildFilterLabelsAsync(IReadOnlyDictionary<string, string> filters, CancellationToken ct)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in filters)
        {
            labels[key] = key switch
            {
                "departmentId" => await db.Departments.AsNoTracking().Where(x => x.Id == value).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "Departamento",
                "localityId" => await db.Localities.AsNoTracking().Where(x => x.Id == value).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "Localidad",
                _ => value
            };
        }
        return labels;
    }

    private static Expression<Func<ExamRollup, string>> SubjectSelector(IReadOnlyDictionary<string, string> subjects)
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

    private static PublicStatsShareMetricDto Metric(double? value, bool suppressed) => suppressed ? new(null, "suppressed") : value is null ? new(null, "unavailable") : new(value, "available");
    internal sealed class GroupAggregate
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public int Attempts { get; init; }
        public double Score { get; init; }
        public double Max { get; init; }
    }
}

using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.TestSupport;
using Xunit;
using Testcontainers.PostgreSql;

namespace PlanCope.Central.Api.Tests;

public sealed partial class StatsQueryControllerTests
{
    [Fact]
    public void PostgresDimensionAndCatalogQueries_TranslateGroupingFilteringAndPaging()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>().UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none").Options;
        using var db = new PlanCopeDbContext(options);
        var rollups = db.ExamRollups.AsNoTracking();
        var aggregateSql = new[]
        {
            rollups.GroupBy(row => row.Course).Select(group => new { Key = group.Key, Total = group.Sum(row => row.AttemptCount) }).OrderBy(row => row.Key).Skip(10).Take(10).ToQueryString(),
            rollups.GroupBy(row => row.SchoolYear).Select(group => new { Key = group.Key, Total = group.Sum(row => row.AttemptCount) }).OrderBy(row => row.Key).Skip(10).Take(10).ToQueryString(),
            rollups.GroupBy(row => row.ExamVersionId).Select(group => new { Key = group.Key, Total = group.Sum(row => row.AttemptCount) }).OrderBy(row => row.Key).Skip(10).Take(10).ToQueryString(),
            rollups.GroupBy(row => row.Cue).Select(group => new { Key = group.Key, Total = group.Sum(row => row.AttemptCount) }).OrderBy(row => row.Key).Skip(10).Take(10).ToQueryString(),
            rollups.GroupBy(row => row.ExamVersionId == "version-1" ? "Ciencias" : "sin_asignar")
                .Select(group => new { Key = group.Key, Total = group.Sum(row => row.AttemptCount) }).OrderBy(row => row.Key).Skip(10).Take(10).ToQueryString(),
            (from rollup in rollups
             join school in db.Schools.AsNoTracking() on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0)) into schoolGroups
             from school in schoolGroups.DefaultIfEmpty()
             join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id into localityGroups
             from locality in localityGroups.DefaultIfEmpty()
             group rollup by new { Key = locality == null ? "sin_asignar" : locality.Id, Label = locality == null ? "sin_asignar" : locality.Name } into grouped
             select new { grouped.Key.Key, grouped.Key.Label, Total = grouped.Sum(row => row.AttemptCount) })
                .OrderBy(row => row.Label).Skip(10).Take(10).ToQueryString(),
            (from rollup in rollups
             join school in db.Schools.AsNoTracking() on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0)) into schoolGroups
             from school in schoolGroups.DefaultIfEmpty()
             join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id into localityGroups
             from locality in localityGroups.DefaultIfEmpty()
             join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id into departmentGroups
             from department in departmentGroups.DefaultIfEmpty()
             group rollup by new { Key = department == null ? "sin_asignar" : department.Id, Label = department == null ? "sin_asignar" : department.Name } into grouped
             select new { grouped.Key.Key, grouped.Key.Label, Total = grouped.Sum(row => row.AttemptCount) })
                .OrderBy(row => row.Label).Skip(10).Take(10).ToQueryString()
        };
        Assert.All(aggregateSql, sql =>
        {
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        });

        var catalogSql = new[]
        {
            rollups.Select(row => row.Course).Distinct().Where(value => value.ToLower().Contains("curso")).OrderBy(value => value).Skip(10).Take(10).ToQueryString(),
            rollups.Select(row => row.SchoolYear).Distinct().Where(value => value.ToLower().Contains("2026")).OrderBy(value => value).Skip(10).Take(10).ToQueryString(),
            rollups.Select(row => row.ExamVersionId).Distinct().Where(value => value.ToLower().Contains("version")).OrderBy(value => value).Skip(10).Take(10).ToQueryString(),
            (from rollup in rollups
             join version in db.ExamVersions.AsNoTracking() on rollup.ExamVersionId equals version.Id
             join exam in db.Exams.AsNoTracking() on version.ExamId equals exam.Id
             select exam.Title + " · Versión " + version.VersionNumber).Distinct().OrderBy(value => value).Skip(10).Take(10).ToQueryString(),
            db.Schools.AsNoTracking().Where(school => school.DeletedAt == null && school.Status == "Active" && school.Name.ToLower().Contains("escuela"))
                .OrderBy(school => school.Name).Skip(10).Take(10).Select(school => school.Cue).ToQueryString(),
            (from rollup in rollups
             join school in db.Schools.AsNoTracking() on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0))
             join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
             select locality.Id).Distinct().OrderBy(value => value).Skip(10).Take(10).ToQueryString(),
            (from rollup in rollups
             join school in db.Schools.AsNoTracking() on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0))
             join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id
             join department in db.Departments.AsNoTracking() on locality.DepartmentId equals department.Id
             select department.Id).Distinct().OrderBy(value => value).Skip(10).Take(10).ToQueryString()
        };
        Assert.All(catalogSql, sql => Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DISTINCT", catalogSql[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", catalogSql[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", catalogSql[5], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", catalogSql[6], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubjectCatalog_UsesVersionMetadataAndPublishedTargetFallback()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.ExamVersions.Add(new ExamVersion("version-current", "exam-current", 1, 1, "Published", JsonDocument.Parse("{\"subject\":\"Ciencias\"}"), null, null, null, null, null, now, now));
        db.ExamVersions.Add(new ExamVersion("version-legacy", "exam-legacy", 1, 1, "Published", null, null, null, null, null, null, now, now));
        db.ExamRollups.AddRange(
            new ExamRollup("rollup-current", "180000100", "2026", "4° grado", "version-current", 8, 4, 8, now),
            new ExamRollup("rollup-legacy", "180000100", "2026", "4° grado", "version-legacy", 8, 4, 8, now));
        var manifest = JsonDocument.Parse("{\"targets\":[{\"targetType\":\"subject\",\"targetId\":\"Lengua\"}]}");
        db.PublicationPackages.Add(new PublicationPackage("package-legacy", "version-legacy", 1, "checksum", manifest, "Published", now, now));
        db.PublicationTargets.Add(new PublicationTarget("target-legacy", "package-legacy", "subject", "Lengua", now, now));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.Catalogs("subject", page: 1, pageSize: 1)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        var secondPage = Assert.IsType<OkObjectResult>((await controller.Catalogs("subject", page: 2, pageSize: 1)).Result);
        using var secondJson = JsonDocument.Parse(JsonSerializer.Serialize(secondPage.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("Lengua", secondJson.RootElement.GetProperty("items")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task SubjectCatalog_IgnoresUnpublishedTargetsAndFallsBackForBlankMetadata()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.ExamVersions.Add(new ExamVersion("version-legacy", "exam-legacy", 1, 1, "Published", JsonDocument.Parse("{\"subject\":\" \"}"), null, null, null, null, null, now, now));
        db.ExamRollups.Add(new ExamRollup("rollup-legacy", "180000100", "2026", "4° grado", "version-legacy", 8, 4, 8, now));
        db.PublicationPackages.AddRange(
            new PublicationPackage("package-published", "version-legacy", 1, "published-checksum", JsonDocument.Parse("{}"), "Published", now, now),
            new PublicationPackage("package-draft", "version-legacy", 2, "draft-checksum", JsonDocument.Parse("{}"), "Draft", now, now));
        db.PublicationTargets.AddRange(
            new PublicationTarget("target-published", "package-published", "subject", "Lengua", now, now),
            new PublicationTarget("target-draft", "package-draft", "subject", "Materia privada", now, now));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.Catalogs("subject", page: 1, pageSize: 10)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var onlySubject = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Lengua", onlySubject.GetProperty("value").GetString());
    }

}

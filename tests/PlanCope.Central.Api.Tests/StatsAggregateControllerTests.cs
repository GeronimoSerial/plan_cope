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
    public async Task Aggregate_RejectsUnknownDimensionAndCapsPageSize()
    {
        using var db = CreateDb();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        Assert.IsType<BadRequestObjectResult>((await controller.Aggregate("anything", page: 1, pageSize: 50)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Aggregate("course", page: 1, pageSize: 101)).Result);
    }

    [Fact]
    public async Task Aggregate_UsesWeightedRollupRatioAndSuppressesSmallProvinceCohort()
    {
        using var db = CreateDb();
        db.ExamRollups.Add(new ExamRollup("rollup-1", "180000100", "2026", "4° grado", "version-1", 3, 3, 6, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.Aggregate("course", page: 1, pageSize: 10)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var row = json.RootElement.GetProperty("rows")[0];
        Assert.Equal("suppressed", row.GetProperty("attemptCount").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("weightedScorePercent").GetProperty("value").ValueKind);
    }

    [Fact]
    public void PostgresGeographyAggregate_TranslatesCanonicalCueJoinGroupAndPage()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>().UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none").Options;
        using var db = new PlanCopeDbContext(options);
        var joined = from rollup in db.ExamRollups.AsNoTracking()
                     join school in db.Schools.AsNoTracking().Where(s => s.DeletedAt == null && s.Status == "Active")
                         on Convert.ToInt64(rollup.Cue) equals (school.Cue > 9_999_999 ? school.Cue : school.Cue * 100 + (school.Annex ?? 0)) into schoolRows
                     from school in schoolRows.DefaultIfEmpty()
                     join locality in db.Localities.AsNoTracking() on school.LocalityId equals locality.Id into localityRows
                     from locality in localityRows.DefaultIfEmpty()
                     select new
                     {
                         Key = locality == null ? "sin_asignar" : locality.Id,
                         Label = locality == null ? "sin_asignar" : locality.Name,
                         rollup.AttemptCount,
                         rollup.ScoreSum,
                         rollup.ScoreMaxSum,
                         rollup.UpdatedAt
                     };
        var sql = joined.GroupBy(row => new { row.Key, row.Label })
            .Select(group => new { group.Key.Key, group.Key.Label, Attempts = group.Sum(row => row.AttemptCount), Score = group.Sum(row => row.ScoreSum), Max = group.Sum(row => row.ScoreMaxSum) })
            .OrderBy(row => row.Label).Skip(50).Take(50).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Aggregate_ReturnsWeightedPercentForAuthorizedSchoolScope()
    {
        using var db = CreateDb();
        db.ExamRollups.Add(new ExamRollup("rollup-1", "180000100", "2026", "4° grado", "version-1", 6, 51, 101, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "school"), new Claim("roster_cue", "180000100")));

        var result = Assert.IsType<OkObjectResult>((await controller.Aggregate("course", page: 1, pageSize: 10)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var row = json.RootElement.GetProperty("rows")[0];
        Assert.Equal(6, row.GetProperty("attemptCount").GetProperty("value").GetInt32());
        Assert.Equal(51d / 101d * 100, row.GetProperty("weightedScorePercent").GetProperty("value").GetDouble(), 6);
    }

    [Fact]
    public async Task VersionOptionsAndAggregateUseExamTitleAndVersionNumber()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Exams.Add(new Exam("exam-version-label", "INTERNAL-CODE", "Biología", null, [], null, null, "Draft", null, now, now));
        db.ExamVersions.Add(new ExamVersion("version-label", "exam-version-label", 3, 1, "Published", null, null, null, null, null, null, now, now));
        db.ExamRollups.Add(new ExamRollup("rollup-version-label", "180000100", "2026", "4° grado", "version-label", 8, 4, 8, now));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var catalogResult = Assert.IsType<OkObjectResult>((await controller.Catalogs("version", page: 1, pageSize: 10)).Result);
        var catalog = Assert.IsType<StatsPageDto<StatsOptionDto>>(catalogResult.Value);
        var option = Assert.Single(catalog.Items);
        Assert.Equal("version-label", option.Value);
        Assert.Equal("Biología · Versión 3", option.Label);

        var aggregateResult = Assert.IsType<OkObjectResult>((await controller.Aggregate("version", page: 1, pageSize: 10)).Result);
        var aggregate = Assert.IsType<StatsAggregateDto>(aggregateResult.Value);
        var row = Assert.Single(aggregate.Rows);
        Assert.Equal("version-label", row.Key);
        Assert.Equal("Biología · Versión 3", row.Label);
    }

    [DockerFact]
    public async Task Aggregate_GroupsAndPagesByCourseInPostgres()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var migrationOptions = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(connectionString, options => options.MigrationsAssembly(typeof(PlanCope.Central.Migrations.Migrations.AddDeliverySessionNodeOwnership).Assembly.GetName().Name))
            .Options;
        var now = DateTimeOffset.UtcNow;
        await using (var seed = new PlanCopeDbContext(migrationOptions))
        {
            await seed.Database.MigrateAsync();
            for (var index = 0; index < 12; index++)
                seed.ExamRollups.Add(new ExamRollup($"rollup-{index}", "180000100", "2026", $"Curso {index % 3}", $"version-{index}", 8, 4, 8, now));
            await seed.SaveChangesAsync();
        }

        var counter = new SqlCommandCounter();
        var queryOptions = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(connectionString, options => options.MigrationsAssembly(typeof(PlanCope.Central.Migrations.Migrations.AddDeliverySessionNodeOwnership).Assembly.GetName().Name))
            .AddInterceptors(counter)
            .Options;
        await using var db = new PlanCopeDbContext(queryOptions);
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.Aggregate("course", page: 2, pageSize: 1)).Result);
        var aggregate = Assert.IsType<StatsAggregateDto>(result.Value);
        Assert.Equal(3, aggregate.TotalCount);
        Assert.Equal("Curso 1", Assert.Single(aggregate.Rows).Label);
        Assert.Contains(counter.Commands, command => command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(counter.Commands, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

}

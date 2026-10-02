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
    public async Task SchoolList_IncludesZeroResultSchoolsAndPaginates()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Provinces.Add(new Province("province-a", "P", "Provincia", now));
        db.Departments.Add(new Department("department-a", "D", "Departamento", "province-a", now));
        db.Localities.AddRange(
            new Locality("locality-a", "department-a", "L1", null, "Localidad A", now),
            new Locality("locality-b", "department-a", "L2", null, "Localidad B", now));
        db.Schools.AddRange(
            new School("school-a", "A", 180000100, null, "Escuela A", "locality-a", "Active", null, now, now),
            new School("school-b", "B", 180000200, null, "Escuela B", "locality-b", "Active", null, now, now));
        db.ExamRollups.Add(new ExamRollup("rollup-a", "180000100", "2026", "4° grado", "version-a", 8, 4, 8, now));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.SchoolList(page: 1, pageSize: 1)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        var row = json.RootElement.GetProperty("items")[0];
        Assert.Equal("180000100", row.GetProperty("cue").GetString());
        Assert.Equal("locality-a", row.GetProperty("localityId").GetString());
        Assert.Equal("Localidad A", row.GetProperty("locality").GetString());
        Assert.Equal("department-a", row.GetProperty("departmentId").GetString());
        Assert.Equal("Departamento", row.GetProperty("department").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("annex").ValueKind);
        Assert.Equal(8, row.GetProperty("attemptCount").GetProperty("value").GetInt32());
        var pageTwoResult = Assert.IsType<OkObjectResult>((await controller.SchoolList(page: 2, pageSize: 1)).Result);
        using var pageTwo = JsonDocument.Parse(JsonSerializer.Serialize(pageTwoResult.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var zeroRow = pageTwo.RootElement.GetProperty("items")[0];
        Assert.Equal("180000200", zeroRow.GetProperty("cue").GetString());
        Assert.Equal(0, zeroRow.GetProperty("attemptCount").GetProperty("value").GetInt32());
        Assert.Equal("unavailable", zeroRow.GetProperty("averageScorePercent").GetProperty("status").GetString());
    }

    [Fact]
    public async Task SchoolList_SchoolScopeReturnsOnlyAuthorizedCueAndAnnex()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Provinces.Add(new Province("province-a", "P", "Provincia", now));
        db.Departments.Add(new Department("department-a", "D", "Departamento", "province-a", now));
        db.Localities.Add(new Locality("locality-a", "department-a", "L", null, "Localidad", now));
        db.Schools.AddRange(
            new School("school-a", "A", 1800001, 5, "Escuela A", "locality-a", "Active", null, now, now),
            new School("school-b", "B", 1800002, 0, "Escuela B", "locality-a", "Active", null, now, now));
        db.ExamRollups.Add(new ExamRollup("rollup-a", "180000105", "2026", "4° grado", "version-a", 8, 4, 8, now));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(
            new Claim("roster_scope", "school"), new Claim("roster_cue", "180000105")));

        var result = Assert.IsType<OkObjectResult>((await controller.SchoolList(page: 1, pageSize: 10)).Result);
        var page = Assert.IsType<StatsPageDto<SchoolStatsListItemDto>>(result.Value);
        var school = Assert.Single(page.Items);
        Assert.Equal("180000105", school.Cue);
        Assert.Equal(5, school.Annex);
        Assert.Equal("locality-a", school.LocalityId);
        Assert.Equal("department-a", school.DepartmentId);
    }

    [DockerFact]
    public async Task SchoolList_UsesBulkPostgresQueriesForGeographyAndRollups()
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
            seed.Provinces.Add(new Province("province-a", "P", "Provincia", now));
            seed.Departments.Add(new Department("department-a", "D", "Departamento", "province-a", now));
            seed.Localities.Add(new Locality("locality-a", "department-a", "L", null, "Localidad", now));
            for (var index = 0; index < 12; index++)
            {
                var baseCue = 1800001L + index;
                var cue = $"{baseCue:D7}00";
                seed.Schools.Add(new School($"school-{index}", $"CODE-{index}", baseCue, null, $"Escuela {index:D2}", "locality-a", "Active", null, now, now));
                seed.ExamRollups.Add(new ExamRollup($"rollup-{index}", cue, "2026", "4° grado", $"version-{index}", 8, 4, 8, now));
            }
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

        var result = Assert.IsType<OkObjectResult>((await controller.SchoolList(page: 1, pageSize: 20)).Result);
        var page = Assert.IsType<StatsPageDto<SchoolStatsListItemDto>>(result.Value);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(12, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("locality-a", item.LocalityId));
        Assert.Equal(5, counter.Commands.Count);
        Assert.Contains(counter.Commands, command => command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(counter.Commands, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

}

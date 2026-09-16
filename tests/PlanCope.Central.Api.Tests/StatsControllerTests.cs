using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class StatsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task GetSchools_WithoutRosterScopeClaim_IsForbidden()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Matematica", "ev-1", attemptCount: 10, scoreSum: 8, scoreMaxSum: 10);

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            Principal(new Claim(ClaimTypes.Role, "Viewer")));

        var result = await controller.GetSchools(null, null, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetSchools_ProvinceScope_SeesCueWithoutRosterCueClaim()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Matematica", "ev-1", attemptCount: 10, scoreSum: 8, scoreMaxSum: 10);

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            Principal(new Claim("roster_scope", "province")));

        var result = await controller.GetSchools(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = ToJson(ok.Value);
        var cues = json.RootElement.EnumerateArray().Select(row => row.GetProperty("cue").GetString()).ToList();
        Assert.Contains("180000100", cues);
    }

    [Fact]
    public async Task GetSchool_SchoolScopeWithDifferentCue_IsForbidden()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Matematica", "ev-1", attemptCount: 10, scoreSum: 8, scoreMaxSum: 10);

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            SchoolPrincipal("180000100"));

        var result = await controller.GetSchool("180000200", null, null, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SmallCohort_IsSuppressedForProvinceButRenderedForOwningSchool()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Matematica", "ev-1", attemptCount: 3, scoreSum: 3, scoreMaxSum: 6);

        using var provinceScope = CreateAuthScope();
        var provinceController = CreateController(
            dbContext,
            provinceScope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            Principal(new Claim("roster_scope", "province")));

        var provinceResult = await provinceController.GetSchools(null, null, CancellationToken.None);
        var provinceOk = Assert.IsType<OkObjectResult>(provinceResult);
        using var provinceJson = ToJson(provinceOk.Value);
        var provinceRow = provinceJson.RootElement
            .EnumerateArray()
            .Single(row => row.GetProperty("cue").GetString() == "180000100");
        Assert.Equal(SuppressibleValue<int>.SuppressionLabel, provinceRow.GetProperty("attemptCount").GetString());

        using var schoolScope = CreateAuthScope();
        var schoolController = CreateController(
            dbContext,
            schoolScope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            SchoolPrincipal("180000100"));

        var schoolResult = await schoolController.GetSchool("180000100", null, null, CancellationToken.None);
        var schoolOk = Assert.IsType<OkObjectResult>(schoolResult);
        using var schoolJson = ToJson(schoolOk.Value);
        Assert.Equal(3, schoolJson.RootElement.GetProperty("attemptCount").GetInt32());
    }

    [Fact]
    public async Task GetCourse_GroupsByCourseAndRendersTotals()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Matematica", "ev-1", attemptCount: 3, scoreSum: 3, scoreMaxSum: 6);
        await SeedRollupAsync(dbContext, "180000100", "2026", "Lengua", "ev-2", attemptCount: 7, scoreSum: 7, scoreMaxSum: 14);

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            Principal(new Claim("roster_scope", "province")));

        var result = await controller.GetCourse("180000100", "2026", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = ToJson(ok.Value);
        var rows = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(7, rows.Single(row => row.GetProperty("course").GetString() == "Lengua").GetProperty("attemptCount").GetInt32());
        Assert.Equal(
            SuppressibleValue<int>.SuppressionLabel,
            rows.Single(row => row.GetProperty("course").GetString() == "Matematica").GetProperty("attemptCount").GetString());
    }

    [Fact]
    public async Task GetExam_ReturnsExamCodeVersionAndBlockBreakdown()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        dbContext.Exams.Add(new Exam("ex-1", "EXA-2026-01", "Matematica", null, null, null, null, "Approved", null, Now, Now));
        dbContext.ExamVersions.Add(new ExamVersion("ev-1", "ex-1", 1, 1, "Published", null, null, null, null, null, null, Now, Now, null));
        var rollupId = Guid.NewGuid().ToString("N");
        dbContext.ExamRollups.Add(new ExamRollup(rollupId, "180000100", "2026", "Matematica", "ev-1", 8, 6, 8, Now));
        dbContext.ExamRollupBlocks.Add(new ExamRollupBlock(Guid.NewGuid().ToString("N"), rollupId, "blk-1", 5, 1, 1, 1, 0, 0, 0));
        await dbContext.SaveChangesAsync();

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            Principal(new Claim("roster_scope", "province")));

        var result = await controller.GetExam("180000100", "2026", "Matematica", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = ToJson(ok.Value);
        var exam = json.RootElement.EnumerateArray().Single();
        Assert.Equal("EXA-2026-01", exam.GetProperty("examCode").GetString());
        Assert.Equal(1, exam.GetProperty("versionNumber").GetInt32());
        Assert.Equal(8, exam.GetProperty("attemptCount").GetInt32());
        var block = exam.GetProperty("blocks").EnumerateArray().Single();
        Assert.Equal("blk-1", block.GetProperty("blockId").GetString());
        Assert.Equal(5, block.GetProperty("correctCount").GetInt32());
    }

    private static async Task SeedRollupAsync(
        PlanCopeDbContext dbContext,
        string cue,
        string schoolYear,
        string course,
        string examVersionId,
        int attemptCount,
        double scoreSum,
        double scoreMaxSum)
    {
        dbContext.ExamRollups.Add(new ExamRollup(
            Guid.NewGuid().ToString("N"),
            cue,
            schoolYear,
            course,
            examVersionId,
            attemptCount,
            scoreSum,
            scoreMaxSum,
            Now));
        await dbContext.SaveChangesAsync();
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static StatsController CreateController(
        PlanCopeDbContext dbContext,
        IAuthorizationService authorizationService,
        ClaimsPrincipal principal)
    {
        return new StatsController(dbContext, authorizationService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static IServiceScope CreateAuthScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RosterCueAccess", policy => policy.Requirements.Add(new RosterScopeRequirement()));
        });
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
    }

    private static ClaimsPrincipal SchoolPrincipal(string cue)
    {
        return Principal(
            new Claim("roster_scope", "school"),
            new Claim("roster_cue", cue));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static JsonDocument ToJson(object? value)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(value));
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Admin;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class SchoolsAdminControllerTests
{
    private const string CueA = "180000100";
    private const string CueB = "180000200";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task SchoolScope_CannotCreateSchool_ReturnsForbidden()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.CreateSchool(
            new SchoolCreateRequest("180000300", "CODE-3", "School Gamma", "loc-3", null),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);

        // The rejection happens before any write: a fresh context on the same database sees no rows.
        using var verify = CreateDbContext(options);
        Assert.False(await verify.Schools.AnyAsync());
    }

    [Fact]
    public async Task AdminRoleOnly_CanCreateAndAdministerSchoolWithoutCueClaim()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var schoolId = SeedSchool(options, CueA, "School Alpha");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var created = await controller.CreateSchool(
            new SchoolCreateRequest(CueB, "CODE-2", "School Beta", "loc-2", null),
            CancellationToken.None);
        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var createdSummary = Assert.IsType<SchoolSummaryDto>(createdResult.Value);
        Assert.Equal(CueB, createdSummary.Cue);

        // The Admin principal holds no roster_cue claim for CueA — the province-only gate used
        // to lock it out of every per-CUE check on this surface.
        var fetched = await controller.GetSchool(schoolId, CancellationToken.None);
        var fetchedOk = Assert.IsType<OkObjectResult>(fetched.Result);
        Assert.Equal(CueA, Assert.IsType<SchoolSummaryDto>(fetchedOk.Value).Cue);

        var update = await controller.UpdateSchool(
            schoolId,
            new SchoolUpdateRequest("School Alpha Renamed", null, "loc-9"),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(update);

        var deactivate = await controller.DeactivateSchool(schoolId, CancellationToken.None);
        Assert.IsType<NoContentResult>(deactivate);

        using var verify = CreateDbContext(options);
        Assert.Equal(2, await verify.Schools.CountAsync());
        var stored = await verify.Schools.SingleAsync(candidate => candidate.Id == schoolId);
        Assert.Equal("School Alpha Renamed", stored.Name);
        Assert.Equal("Inactive", stored.Status);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task ProvinceScope_CanCreateAndListSchools()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedSchool(options, CueA, "School Alpha");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var created = await controller.CreateSchool(
            new SchoolCreateRequest(CueB, "CODE-2", "School Beta", "loc-2", 1),
            CancellationToken.None);
        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var summary = Assert.IsType<SchoolSummaryDto>(createdResult.Value);
        Assert.Equal(CueB, summary.Cue);
        Assert.Equal("Active", summary.Status);

        // Province scope sees every CUE, including the school just created.
        var list = await controller.ListSchools(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        Assert.Equal(2, Assert.IsType<List<SchoolSummaryDto>>(ok.Value).Count);

        using var verify = CreateDbContext(options);
        Assert.True(await verify.AuditLogs.AnyAsync(log => log.Action == "admin.school.create"));
    }

    [Fact]
    public async Task SchoolScope_ListSchools_OnlySeesOwnCue()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedSchool(options, CueA, "School Alpha");
        SeedSchool(options, CueB, "School Beta");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListSchools(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var schools = Assert.IsType<List<SchoolSummaryDto>>(ok.Value);
        var visible = Assert.Single(schools);
        Assert.Equal(CueA, visible.Cue);
        Assert.DoesNotContain(schools, school => school.Cue == CueB);
    }

    [Fact]
    public async Task CreateSchool_DuplicateCueInDifferentTextualForm_ReturnsConflict()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedSchool(options, CueA, "School Alpha");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        // "1800001-00" normalizes to the same 9 digits as the seeded CueA.
        var result = await controller.CreateSchool(
            new SchoolCreateRequest("1800001-00", "CODE-DUP", "Duplicate Cue", "loc-1", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        using var verify = CreateDbContext(options);
        Assert.Equal(1, await verify.Schools.CountAsync());
    }

    [Fact]
    public async Task CreateSchool_InvalidCue_ReturnsBadRequest()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.CreateSchool(
            new SchoolCreateRequest("12345", "CODE-X", "Invalid Cue", "loc-1", null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ProvinceScope_CanUpdateAndDeactivateSchool()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var schoolId = SeedSchool(options, CueA, "School Alpha");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var update = await controller.UpdateSchool(
            schoolId,
            new SchoolUpdateRequest("School Alpha Renamed", null, "loc-9"),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(update);

        var deactivate = await controller.DeactivateSchool(schoolId, CancellationToken.None);
        Assert.IsType<NoContentResult>(deactivate);

        using var verify = CreateDbContext(options);
        var school = await verify.Schools.SingleAsync();
        Assert.Equal("School Alpha Renamed", school.Name);
        Assert.Equal("Inactive", school.Status);
        Assert.Null(school.DeletedAt);
        Assert.Equal(2, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task DeactivateSchool_AlreadyInactive_ReturnsNoContent()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var schoolId = SeedSchool(options, CueA, "School Alpha", status: "Inactive");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var first = await controller.DeactivateSchool(schoolId, CancellationToken.None);
        Assert.IsType<NoContentResult>(first);

        var second = await controller.DeactivateSchool(schoolId, CancellationToken.None);
        Assert.IsType<NoContentResult>(second);

        using var verify = CreateDbContext(options);
        var school = await verify.Schools.SingleAsync();
        Assert.Equal("Inactive", school.Status);
        // The no-op path performs no mutation, so it writes no audit row either.
        Assert.False(await verify.AuditLogs.AnyAsync());
    }

    private static string SeedSchool(DbContextOptions<PlanCopeDbContext> options, string cue, string name, string status = "Active")
    {
        using var dbContext = CreateDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var school = new School(
            NewId(),
            $"CODE-{cue}",
            long.Parse(cue, System.Globalization.CultureInfo.InvariantCulture),
            null,
            name,
            "loc-1",
            status,
            null,
            now,
            now);
        dbContext.Schools.Add(school);
        dbContext.SaveChanges();
        return school.Id;
    }

    private static IServiceScope CreateAuthorizationScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static PlanCopeDbContext CreateDbContext(DbContextOptions<PlanCopeDbContext> options)
    {
        return new PlanCopeDbContext(options);
    }

    private static SchoolsAdminController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal,
        IAuthorizationService authorizationService)
    {
        return new SchoolsAdminController(dbContext, authorizationService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static ClaimsPrincipal ProvincePrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "RosterProvince"),
            new Claim("roster_scope", "province"));
    }

    private static ClaimsPrincipal SchoolPrincipal(params string[] cues)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Viewer"),
            new("roster_scope", "school")
        };
        claims.AddRange(cues.Select(static cue => new Claim("roster_cue", cue)));
        return Principal(claims.ToArray());
    }

    // The Central administrator: Admin is the codebase's unbounded-authority role, and
    // AuthController maps it to school roster scope with no roster_cue claims at all.
    private static ClaimsPrincipal AdminPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("roster_scope", "school"));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

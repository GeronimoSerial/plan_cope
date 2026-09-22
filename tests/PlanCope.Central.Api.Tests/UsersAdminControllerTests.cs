using System.Reflection;
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

public sealed class UsersAdminControllerTests
{
    private const string CueA = "180000100";
    private const string CueB = "180000200";

    // Seeded users are never logged in during these tests, so a syntactically plausible BCrypt
    // hash is enough for fixtures. Runs no BCrypt work at rest.
    private const string FakePasswordHash = "$2a$11$invalidinvalidinvalidinvalidinvalidinvalidinvalidin";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task SchoolScope_CannotCreateUser_ReturnsForbidden()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.CreateUser(
            new UserCreateRequest("new@school.test", "Password123!", "New User"),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);

        // The rejection happens before any write: a fresh context on the same database sees no rows.
        using var verify = CreateDbContext(options);
        Assert.False(await verify.Users.AnyAsync());
    }

    [Fact]
    public async Task SchoolScope_ListUsers_OnlySeesUsersWithOwnCue()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedUser(options, "alpha@school.test", CueA);
        SeedUser(options, "beta@school.test", CueB);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListUsers(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var users = Assert.IsType<List<UserSummaryDto>>(ok.Value);
        var visible = Assert.Single(users);
        Assert.Equal("alpha@school.test", visible.Email);
        Assert.DoesNotContain(users, user => user.Email == "beta@school.test");
    }

    [Fact]
    public async Task SchoolScope_AssignCueOutsideOwnClaims_ReturnsForbiddenAndWritesNoRow()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "alpha@school.test", CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.AssignSchool(userId, new AssignSchoolRequest(CueB), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        // Verify against a fresh DbContext on the same in-memory database: the forbidden
        // assignment must NOT have been written.
        using var verify = CreateDbContext(options);
        Assert.False(await verify.UserSchools.AnyAsync(
            assignment => assignment.UserId == userId && assignment.Cue == CueB));
        Assert.Equal(1, await verify.UserSchools.CountAsync(assignment => assignment.UserId == userId));
    }

    [Fact]
    public async Task SchoolScope_AssignUnboundedRoleToOwnUser_ReturnsForbiddenAndWritesNoRow()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "self@school.test", CueA);
        var roleId = SeedRole(options, "RosterProvince");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.AssignRole(userId, new AssignRoleRequest("RosterProvince"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        // Verify against a fresh DbContext on the same in-memory database: the forbidden
        // assignment must NOT have been written.
        using var verify = CreateDbContext(options);
        Assert.False(await verify.UserRoles.AnyAsync(
            assignment => assignment.UserId == userId && assignment.RoleId == roleId));
        Assert.Equal(0, await verify.UserRoles.CountAsync());
    }

    [Fact]
    public async Task SchoolScope_AssignAdminRoleToOwnUser_ReturnsForbiddenAndWritesNoRow()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "self@school.test", CueA);
        var roleId = SeedRole(options, "Admin");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.AssignRole(userId, new AssignRoleRequest("Admin"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        using var verify = CreateDbContext(options);
        Assert.False(await verify.UserRoles.AnyAsync(
            assignment => assignment.UserId == userId && assignment.RoleId == roleId));
        Assert.Equal(0, await verify.UserRoles.CountAsync());
    }

    [Fact]
    public async Task SchoolScope_AssignUnboundedRoleToOtherUserSharingCue_ReturnsForbiddenAndWritesNoRow()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedUser(options, "self@school.test", CueA);
        var otherUserId = SeedUser(options, "other@school.test", CueA);
        var roleId = SeedRole(options, "RosterProvince");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        // The escalation is not limited to self-targeting: a shared CUE must not be enough to
        // grant an unbounded role to anyone.
        var result = await controller.AssignRole(otherUserId, new AssignRoleRequest("RosterProvince"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        using var verify = CreateDbContext(options);
        Assert.False(await verify.UserRoles.AnyAsync(
            assignment => assignment.UserId == otherUserId && assignment.RoleId == roleId));
        Assert.Equal(0, await verify.UserRoles.CountAsync());
    }

    [Fact]
    public async Task SchoolScope_AssignBoundedRoleToUserSharingCue_Succeeds()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "other@school.test", CueA);
        var roleId = SeedRole(options, "Viewer");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.AssignRole(userId, new AssignRoleRequest("Viewer"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        using var verify = CreateDbContext(options);
        Assert.Equal(1, await verify.UserRoles.CountAsync(
            assignment => assignment.UserId == userId && assignment.RoleId == roleId));
    }

    [Fact]
    public async Task AdminRoleOnly_CanCreateAndManageUserWithNoCueAssignment()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedRole(options, "Viewer");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var created = await controller.CreateUser(
            new UserCreateRequest("new@plancope.test", "Password123!", "New User"),
            CancellationToken.None);
        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var summary = Assert.IsType<UserSummaryDto>(createdResult.Value);

        // The created user has zero CUE assignments — the case a province-only gate used to
        // lock the Admin role out of entirely.
        var fetched = await controller.GetUser(summary.Id, CancellationToken.None);
        var fetchedOk = Assert.IsType<OkObjectResult>(fetched.Result);
        Assert.Equal(summary.Id, Assert.IsType<UserSummaryDto>(fetchedOk.Value).Id);

        Assert.IsType<NoContentResult>(await controller.AssignRole(summary.Id, new AssignRoleRequest("Viewer"), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.DeactivateUser(summary.Id, CancellationToken.None));

        using var verify = CreateDbContext(options);
        var stored = await verify.Users.SingleAsync(candidate => candidate.Email == "new@plancope.test");
        Assert.Equal("Inactive", stored.Status);
        Assert.Equal(0, await verify.UserSchools.CountAsync());
        Assert.Equal(1, await verify.UserRoles.CountAsync());
    }

    [Fact]
    public async Task AdminRoleOnly_ListUsers_SeesEverySeededUser()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedUser(options, "alpha@school.test", CueA);
        SeedUser(options, "beta@school.test", CueB);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListUsers(CancellationToken.None);

        // The Central Admin holds no roster_cue claims, so the school-scope branch would build
        // an empty CUE set and hide every user — the endpoint must instead return them all.
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var users = Assert.IsType<List<UserSummaryDto>>(ok.Value);
        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Email == "alpha@school.test");
        Assert.Contains(users, user => user.Email == "beta@school.test");
    }

    [Fact]
    public async Task ProvinceScope_CanCreateUserAssignRoleAndCue()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        SeedUser(options, "alpha@school.test", CueA);
        SeedRole(options, "Viewer");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var created = await controller.CreateUser(
            new UserCreateRequest("new@plancope.test", "Password123!", "New User"),
            CancellationToken.None);
        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var summary = Assert.IsType<UserSummaryDto>(createdResult.Value);
        Assert.Empty(summary.Cues);
        Assert.Empty(summary.RoleCodes);

        var assignRole = await controller.AssignRole(summary.Id, new AssignRoleRequest("Viewer"), CancellationToken.None);
        Assert.IsType<NoContentResult>(assignRole);

        // Province scope may grant any CUE, including one outside any school roster.
        var assignCue = await controller.AssignSchool(summary.Id, new AssignSchoolRequest(CueB), CancellationToken.None);
        Assert.IsType<NoContentResult>(assignCue);

        // Province scope lists every user, across every CUE.
        var list = await controller.ListUsers(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var users = Assert.IsType<List<UserSummaryDto>>(ok.Value);
        Assert.Equal(2, users.Count);
        var createdRow = Assert.Single(users, user => user.Email == "new@plancope.test");
        Assert.Equal(CueB, Assert.Single(createdRow.Cues));
        Assert.Equal("Viewer", Assert.Single(createdRow.RoleCodes));

        using var verify = CreateDbContext(options);
        var stored = await verify.Users.SingleAsync(candidate => candidate.Email == "new@plancope.test");
        Assert.NotEqual("Password123!", stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123!", stored.PasswordHash));
        Assert.Equal(1, await verify.UserRoles.CountAsync());
        Assert.Equal(2, await verify.UserSchools.CountAsync());
        Assert.True(await verify.AuditLogs.AnyAsync(log => log.Action == "admin.user.create"));
    }

    [Fact]
    public async Task Mutations_AreIdempotentOnSecondCall()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "target@plancope.test", CueA);
        SeedRole(options, "Viewer");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        Assert.IsType<NoContentResult>(await controller.DeactivateUser(userId, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.DeactivateUser(userId, CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.AssignRole(userId, new AssignRoleRequest("Viewer"), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.AssignRole(userId, new AssignRoleRequest("Viewer"), CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.RevokeRole(userId, "Viewer", CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.RevokeRole(userId, "Viewer", CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.RevokeRole(userId, "NoSuchRole", CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.AssignSchool(userId, new AssignSchoolRequest(CueA), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.AssignSchool(userId, new AssignSchoolRequest(CueB), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.AssignSchool(userId, new AssignSchoolRequest(CueB), CancellationToken.None));

        Assert.IsType<NoContentResult>(await controller.RevokeSchool(userId, CueB, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.RevokeSchool(userId, CueB, CancellationToken.None));

        using var verify = CreateDbContext(options);
        var stored = await verify.Users.SingleAsync();
        Assert.Equal("Inactive", stored.Status);
        Assert.Equal(0, await verify.UserRoles.CountAsync());
        // CueA was already assigned (idempotent no-op) and CueB was assigned then revoked.
        Assert.Equal(1, await verify.UserSchools.CountAsync());
    }

    [Fact]
    public async Task ResetPassword_StoresBcryptHashAndAuditOmitsPassword()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "target@plancope.test", CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.ResetPassword(userId, new ResetPasswordRequest("NewSecret123!"), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        using var verify = CreateDbContext(options);
        var stored = await verify.Users.SingleAsync();
        Assert.NotEqual("NewSecret123!", stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewSecret123!", stored.PasswordHash));

        var audit = await verify.AuditLogs.SingleAsync(log => log.Action == "admin.user.reset_password");
        Assert.DoesNotContain("NewSecret123!", audit.Payload!.RootElement.GetRawText());
    }

    [Fact]
    public async Task ResetPassword_ShortNewPassword_ReturnsBadRequest()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var userId = SeedUser(options, "target@plancope.test", CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.ResetPassword(userId, new ResetPasswordRequest("short"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void UserSummaryDto_NeverCarriesPasswordMaterial()
    {
        var properties = typeof(UserSummaryDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, static name => name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, static name => name.Contains("Password", StringComparison.OrdinalIgnoreCase));

        // Closed property set: any future addition of credential material must fail this test.
        var allowed = new[]
        {
            "Id", "Email", "FullName", "Status", "Cues", "RoleCodes"
        };
        Assert.Equal(allowed.OrderBy(static name => name), properties.OrderBy(static name => name));
    }

    private static string SeedUser(DbContextOptions<PlanCopeDbContext> options, string email, string? cue = null)
    {
        using var dbContext = CreateDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var user = new User(
            NewId(),
            email,
            FakePasswordHash,
            email,
            "Active",
            null,
            null,
            now,
            now);
        dbContext.Users.Add(user);
        if (cue is not null)
        {
            dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, cue, now));
        }
        dbContext.SaveChanges();
        return user.Id;
    }

    private static string SeedRole(DbContextOptions<PlanCopeDbContext> options, string code)
    {
        using var dbContext = CreateDbContext(options);
        var role = new Role(NewId(), code, code, null, DateTimeOffset.UtcNow);
        dbContext.Roles.Add(role);
        dbContext.SaveChanges();
        return role.Id;
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

    private static UsersAdminController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal,
        IAuthorizationService authorizationService)
    {
        return new UsersAdminController(dbContext, authorizationService)
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

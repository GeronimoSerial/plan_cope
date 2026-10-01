using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_WithRosterProvinceRole_SetsProvinceScopeAndNoCues()
    {
        using var dbContext = CreateDbContext();
        var provinceRole = new Role(NewId(), "RosterProvince", "Alcance provincial de padrón", null, DateTimeOffset.UtcNow);
        var user = CreateUser("province@plancope.test", "Usuario de Planeamiento");
        dbContext.Roles.Add(provinceRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, provinceRole.Id, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var response = AssertLoginResponse(result);
        var profile = response.User;
        Assert.Equal("province", profile.RosterScope);
        Assert.Empty(profile.RosterCues);
        AssertTokenScope(response.AccessToken, "province");
    }

    [Fact]
    public async Task Login_WithSchoolUserAndAssignedCue_SetsSchoolScopeWithCuesInProfileAndToken()
    {
        using var dbContext = CreateDbContext();
        var viewerRole = new Role(NewId(), "Viewer", "Visor de escuela", null, DateTimeOffset.UtcNow);
        var user = CreateUser("school@plancope.test", "Usuario de escuela");
        dbContext.Roles.Add(viewerRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, viewerRole.Id, DateTimeOffset.UtcNow));
        dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, "180000100", DateTimeOffset.UtcNow));
        dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, "180000200", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var response = AssertLoginResponse(result);
        var profile = response.User;
        Assert.Equal("school", profile.RosterScope);
        Assert.Equal(new[] { "180000100", "180000200" }, profile.RosterCues);
        AssertTokenScope(response.AccessToken, "school", "180000100", "180000200");
    }

    [Fact]
    public async Task Login_WithSchoolUserWithoutAssignedCues_SetsProvinceScopeInProfileAndToken()
    {
        using var dbContext = CreateDbContext();
        var viewerRole = new Role(NewId(), "Viewer", "Visor de escuela", null, DateTimeOffset.UtcNow);
        var user = CreateUser("empty-school@plancope.test", "Usuario de escuela sin CUEs");
        dbContext.Roles.Add(viewerRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, viewerRole.Id, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var response = AssertLoginResponse(result);
        var profile = response.User;
        Assert.Equal("province", profile.RosterScope);
        Assert.Empty(profile.RosterCues);
        AssertTokenScope(response.AccessToken, "province");
        AssertTokenScope(response.RefreshToken!, "province");

        var refreshed = await controller.Refresh(
            new RefreshTokenRequest(response.RefreshToken!), CancellationToken.None);
        var refreshedResponse = AssertLoginResponse(refreshed);
        Assert.Equal("province", refreshedResponse.User.RosterScope);
        AssertTokenScope(refreshedResponse.AccessToken, "province");
    }

    private const string Password = "Secret123!";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
        return new PlanCopeDbContext(options);
    }

    private static AuthController CreateController(PlanCopeDbContext dbContext)
    {
        var options = Options.Create(new AuthOptions
        {
            SigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        var tokenService = new TokenService(options);
        return new AuthController(dbContext, tokenService);
    }

    private static User CreateUser(string email, string fullName)
    {
        var now = DateTimeOffset.UtcNow;
        return new User(
            NewId(),
            email,
            BCrypt.Net.BCrypt.HashPassword(Password),
            fullName,
            "Active",
            null,
            null,
            now,
            now);
    }

    private static LoginResponse AssertLoginResponse(ActionResult<LoginResponse> result)
    {
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<LoginResponse>(okResult.Value);
    }

    private static void AssertTokenScope(string token, string scope, params string[] cues)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, claim => claim.Type == "roster_scope" && claim.Value == scope);
        Assert.Equal(cues.Order(), jwt.Claims.Where(claim => claim.Type == "roster_cue")
            .Select(claim => claim.Value).Order());
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

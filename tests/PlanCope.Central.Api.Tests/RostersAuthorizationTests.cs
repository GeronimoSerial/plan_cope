using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Domain.Central;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RostersAuthorizationTests
{
    [Fact]
    public async Task SchoolUser_RequestingForeignCue_IsForbidden()
    {
        using var scope = CreateScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            new FakeRosterService(snapshot: null),
            SchoolPrincipal());

        var result = await controller.Status("180000200", "2026", CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task SchoolUser_RequestingOwnCue_IsAllowed()
    {
        using var scope = CreateScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            new FakeRosterService(Snapshot("180000100")),
            SchoolPrincipal());

        var result = await controller.Status("180000100", "2026", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task ProvinceUser_RequestingAnyCue_IsAllowed()
    {
        using var scope = CreateScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            new FakeRosterService(Snapshot("180000200")),
            ProvincePrincipal());

        var result = await controller.Status("180000200", "2026", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task User_WithoutRosterScopeClaim_IsForbidden()
    {
        using var scope = CreateScope();
        var controller = CreateController(
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            new FakeRosterService(snapshot: null),
            Principal());

        var result = await controller.Status("180000100", "2026", CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task RolePolicy_DeniesUserWithoutRequiredRole()
    {
        using var scope = CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var viewerPrincipal = Principal(new Claim(ClaimTypes.Role, "Viewer"));

        var result = await authorizationService.AuthorizeAsync(viewerPrincipal, resource: null, "Admin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RolePolicy_AllowsUserWithRequiredRole()
    {
        using var scope = CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var adminPrincipal = Principal(new Claim(ClaimTypes.Role, "Admin"));

        var result = await authorizationService.AuthorizeAsync(adminPrincipal, resource: null, "Admin");

        Assert.True(result.Succeeded);
    }

    private static RostersController CreateController(
        IAuthorizationService authorizationService,
        IGeRosterService rosterService,
        ClaimsPrincipal principal)
    {
        var controller = new RostersController(rosterService, authorizationService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
        return controller;
    }

    private static IServiceScope CreateScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RosterCueAccess", policy => policy.Requirements.Add(new RosterScopeRequirement()));
            options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
            options.AddPolicy("Viewer", policy => policy.RequireRole("Viewer"));
            options.AddPolicy("RosterProvince", policy => policy.RequireRole("RosterProvince"));
        });
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
    }

    private static ClaimsPrincipal SchoolPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim("roster_scope", "school"),
            new Claim("roster_cue", "180000100"));
    }

    private static ClaimsPrincipal ProvincePrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "RosterProvince"),
            new Claim("roster_scope", "province"));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static GeRosterSnapshot Snapshot(string cue)
    {
        return new GeRosterSnapshot
        {
            Id = "snapshot-1",
            Cue = cue,
            SchoolYear = "2026",
            FetchedAt = DateTimeOffset.UtcNow,
            Checksum = "abc123",
            SectionCount = 0,
            StudentCount = 0,
            Status = "Synced"
        };
    }

    private sealed class FakeRosterService(GeRosterSnapshot? snapshot) : IGeRosterService
    {
        public Task<GeRosterRefreshResult> RefreshAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }

        public Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GeRosterSectionStatus> sections = [];
            return Task.FromResult(sections);
        }
    }
}
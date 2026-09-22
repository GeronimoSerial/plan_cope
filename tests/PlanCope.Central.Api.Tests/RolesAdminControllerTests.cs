using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Admin;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RolesAdminControllerTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task ProvinceScope_ListRoles_ReturnsEverySeededRole()
    {
        var options = CreateOptions();
        var seeded = SeedRoles(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal());

        var result = await controller.ListRoles(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<List<RoleSummaryDto>>(ok.Value);
        Assert.Equal(seeded.Count, roles.Count);
        foreach (var role in seeded)
        {
            var dto = Assert.Single(roles, candidate => candidate.Code == role.Code);
            Assert.Equal(role.Id, dto.Id);
            Assert.Equal(role.Name, dto.Name);
            Assert.Equal(role.Description, dto.Description);
        }
    }

    [Fact]
    public async Task SchoolScope_ListRoles_ReturnsEverySeededRole()
    {
        var options = CreateOptions();
        var seeded = SeedRoles(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal("180000100"));

        var result = await controller.ListRoles(CancellationToken.None);

        // The endpoint has no scope restriction: a school-scope caller reads the same
        // reference data it needs to build an assign-role request.
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsType<List<RoleSummaryDto>>(ok.Value);
        Assert.Equal(seeded.Count, roles.Count);
        foreach (var role in seeded)
        {
            var dto = Assert.Single(roles, candidate => candidate.Code == role.Code);
            Assert.Equal(role.Id, dto.Id);
            Assert.Equal(role.Name, dto.Name);
            Assert.Equal(role.Description, dto.Description);
        }
    }

    private static IReadOnlyList<Role> SeedRoles(DbContextOptions<PlanCopeDbContext> options)
    {
        using var dbContext = CreateDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var roles = new List<Role>
        {
            new(NewId(), "Admin", "Administrator", "Unbounded administrative access.", now),
            new(NewId(), "RosterProvince", "Province roster reader", "Province-wide roster read access.", now),
            new(NewId(), "Viewer", "Viewer", null, now)
        };
        dbContext.Roles.AddRange(roles);
        dbContext.SaveChanges();
        return roles;
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

    private static RolesAdminController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal)
    {
        return new RolesAdminController(dbContext)
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

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

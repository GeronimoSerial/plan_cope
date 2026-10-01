using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

public sealed class ReleaseRingsAdminControllerTests
{
    private const string AdminUserId = "11111111-1111-1111-1111-111111111111";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Admin_ListReleaseRings_ReturnsEverySeededRingMappedInOrder()
    {
        var options = CreateOptions();
        var seeded = SeedRings(options);

        using var adminContext = CreateDbContext(options);
        var adminList = await CreateController(adminContext, AdminPrincipal())
            .ListReleaseRings(CancellationToken.None);
        AssertSeededRingsInOrder(seeded, adminList);
    }

    [Fact]
    public async Task Admin_CreateReleaseRing_AllEnrolled_PersistsWithoutPercentage()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.0.0",
                "stable",
                "AllEnrolled",
                null),
            CancellationToken.None);

        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var dto = Assert.IsType<ReleaseRingSummaryDto>(createdResult.Value);
        Assert.Equal("2.0.0", dto.Version);
        Assert.Equal("stable", dto.Channel);

        using var verify = CreateDbContext(options);
        var stored = await verify.ReleaseRings.SingleAsync();
        Assert.Equal("AllEnrolled", stored.RolloutMode);
        Assert.Null(stored.RolloutPercentage);
        Assert.Equal(Guid.Parse(AdminUserId), stored.CreatedBy);
        Assert.True(await verify.AuditLogs.AnyAsync(log => log.Action == "admin.release_ring.create"));
    }

    [Fact]
    public async Task Admin_CreateReleaseRing_PercentageOfEnrolled_PersistsPercentage()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.5.0",
                "beta",
                "PercentageOfEnrolled",
                25),
            CancellationToken.None);

        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var dto = Assert.IsType<ReleaseRingSummaryDto>(createdResult.Value);
        Assert.Equal(25, dto.RolloutPercentage);

        using var verify = CreateDbContext(options);
        var stored = await verify.ReleaseRings.SingleAsync();
        Assert.Equal("PercentageOfEnrolled", stored.RolloutMode);
        Assert.Equal(25, stored.RolloutPercentage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(150)]
    [InlineData(-1)]
    public async Task Admin_CreateReleaseRing_PercentageOfEnrolledWithoutValidPercentage_ReturnsBadRequest(
        int? rolloutPercentage)
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.5.0",
                "stable",
                "PercentageOfEnrolled",
                rolloutPercentage),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(created.Result);

        // The rejection happens before any write: a fresh context on the same database sees no rows.
        using var verify = CreateDbContext(options);
        Assert.False(await verify.ReleaseRings.AnyAsync());
        Assert.False(await verify.AuditLogs.AnyAsync());
    }

    [Theory]
    [InlineData("Canary")]
    [InlineData("ExplicitList")]
    public async Task Admin_CreateReleaseRing_InvalidRolloutMode_ReturnsBadRequest(string rolloutMode)
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.5.0",
                "stable",
                rolloutMode,
                null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(created.Result);

        using var verify = CreateDbContext(options);
        Assert.False(await verify.ReleaseRings.AnyAsync());
        Assert.False(await verify.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task Admin_CreateReleaseRing_DoesNotRequirePackageMetadata()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.5.0",
                "stable",
                "AllEnrolled",
                null),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(created.Result).StatusCode);
        using var verify = CreateDbContext(options);
        var stored = await verify.ReleaseRings.SingleAsync();
        Assert.Equal(string.Empty, stored.Sha256);
        Assert.Equal(string.Empty, stored.DownloadUrl);
    }

    [Fact]
    public async Task Admin_CreateReleaseRing_UsesReleaseVersionAndChannelAsItsPackageReference()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.5.0",
                "stable",
                "AllEnrolled",
                null),
            CancellationToken.None);

        var createdResult = Assert.IsType<ObjectResult>(created.Result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var dto = Assert.IsType<ReleaseRingSummaryDto>(createdResult.Value);
        Assert.Equal("2.5.0", dto.Version);
        Assert.Equal("stable", dto.Channel);

        using var verify = CreateDbContext(options);
        var stored = await verify.ReleaseRings.SingleAsync();
        Assert.Equal(string.Empty, stored.Sha256);
        Assert.Equal(string.Empty, stored.DownloadUrl);
    }

    [Fact]
    public async Task Admin_UpdateReleaseRing_ChangesRolloutPolicyOnly()
    {
        var options = CreateOptions();
        var seeded = SeedRing(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var update = await controller.UpdateReleaseRing(
            seeded.Id,
            new ReleaseRingUpdateRequest("PercentageOfEnrolled", 30),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(update);

        using var verify = CreateDbContext(options);
        var stored = await verify.ReleaseRings.SingleAsync(candidate => candidate.Id == seeded.Id);
        Assert.Equal("PercentageOfEnrolled", stored.RolloutMode);
        Assert.Equal(30, stored.RolloutPercentage);
        // Version and Channel are immutable: the update only changes rollout policy.
        Assert.Equal(seeded.Version, stored.Version);
        Assert.Equal(seeded.Channel, stored.Channel);
        Assert.Equal(seeded.Sha256, stored.Sha256);
        Assert.Equal(seeded.DownloadUrl, stored.DownloadUrl);
        Assert.Equal(seeded.CreatedAt, stored.CreatedAt);
        Assert.Equal(seeded.CreatedBy, stored.CreatedBy);
        Assert.True(await verify.AuditLogs.AnyAsync(log => log.Action == "admin.release_ring.update"));
    }

    [Fact]
    public async Task Admin_UpdateReleaseRing_UnknownId_ReturnsNotFound()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal());

        var update = await controller.UpdateReleaseRing(
            Guid.NewGuid(),
            new ReleaseRingUpdateRequest("AllEnrolled", null),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(update);
    }

    [Fact]
    public async Task SchoolScopeCaller_CannotListCreateOrUpdate_ReturnsForbiddenAndWritesNothing()
    {
        await AssertForbiddenWithoutWrites(ViewerPrincipal());
    }

    [Fact]
    public async Task ProvinceScopeCaller_CannotListCreateOrUpdate_ReturnsForbiddenAndWritesNothing()
    {
        await AssertForbiddenWithoutWrites(ProvincePrincipal());
    }

    [Fact]
    public async Task Admin_CreateReleaseRing_MissingNameIdentifierClaim_ReturnsUnauthorized()
    {
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminWithoutNameIdentifierPrincipal());

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "2.0.0",
                "stable",
                "AllEnrolled",
                null),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(created.Result);

        using var verify = CreateDbContext(options);
        Assert.False(await verify.ReleaseRings.AnyAsync());
    }

    private static void AssertSeededRingsInOrder(
        IReadOnlyList<ReleaseRing> seeded,
        ActionResult<IReadOnlyList<ReleaseRingSummaryDto>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rings = Assert.IsType<List<ReleaseRingSummaryDto>>(ok.Value);

        // Ordered by Channel ascending, then CreatedAt descending.
        var expected = seeded
            .OrderBy(static ring => ring.Channel, StringComparer.Ordinal)
            .ThenByDescending(static ring => ring.CreatedAt)
            .ToList();
        Assert.Equal(expected.Count, rings.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id, rings[i].Id);
        }

        foreach (var ring in seeded)
        {
            var dto = Assert.Single(rings, candidate => candidate.Id == ring.Id);
            Assert.Equal(ring.Version, dto.Version);
            Assert.Equal(ring.Channel, dto.Channel);
            Assert.Equal(ring.Sha256, dto.Sha256);
            Assert.Equal(ring.DownloadUrl, dto.DownloadUrl);
            Assert.Equal(ring.RolloutMode, dto.RolloutMode);
            Assert.Equal(ring.RolloutPercentage, dto.RolloutPercentage);
            Assert.Equal(ring.CreatedAt, dto.CreatedAt);
            Assert.Equal(ring.CreatedBy, dto.CreatedBy);
        }
    }

    // A non-Admin principal — school-scope viewer or province-scope roster reader — must get
    // Forbid() on list, create AND update: this surface is Admin-only, with no roster-scope
    // fallback path, and the rejection must happen before any row or audit entry is written.
    private static async Task AssertForbiddenWithoutWrites(ClaimsPrincipal principal)
    {
        var options = CreateOptions();
        var seeded = SeedRing(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, principal);

        var list = await controller.ListReleaseRings(CancellationToken.None);
        Assert.IsType<ForbidResult>(list.Result);

        var created = await controller.CreateReleaseRing(
            new ReleaseRingCreateRequest(
                "9.9.9",
                "stable",
                "AllEnrolled",
                null),
            CancellationToken.None);
        Assert.IsType<ForbidResult>(created.Result);

        var update = await controller.UpdateReleaseRing(
            seeded.Id,
            new ReleaseRingUpdateRequest("PercentageOfEnrolled", 90),
            CancellationToken.None);
        Assert.IsType<ForbidResult>(update);

        // The rejection happens before any write: a fresh context on the same database sees the
        // seeded ring untouched and no ring added by the rejected POST.
        using var verify = CreateDbContext(options);
        Assert.Equal(1, await verify.ReleaseRings.CountAsync());
        var stored = await verify.ReleaseRings.SingleAsync(candidate => candidate.Id == seeded.Id);
        Assert.Equal(seeded.RolloutMode, stored.RolloutMode);
        Assert.Equal(seeded.RolloutPercentage, stored.RolloutPercentage);
        Assert.False(await verify.AuditLogs.AnyAsync());
    }

    private static IReadOnlyList<ReleaseRing> SeedRings(DbContextOptions<PlanCopeDbContext> options)
    {
        using var dbContext = CreateDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var rings = new List<ReleaseRing>
        {
            new(
                Guid.NewGuid(),
                "2.0.0",
                "stable",
                Sha256Of("2.0.0"),
                $"https://downloads.example.test/stable/2.0.0",
                "AllEnrolled",
                null,
                now.AddMinutes(-10),
                Guid.NewGuid()),
            new(
                Guid.NewGuid(),
                "1.5.0",
                "stable",
                Sha256Of("1.5.0"),
                $"https://downloads.example.test/stable/1.5.0",
                "PercentageOfEnrolled",
                25,
                now.AddMinutes(-20),
                Guid.NewGuid()),
            new(
                Guid.NewGuid(),
                "3.0.0-beta.1",
                "beta",
                Sha256Of("3.0.0-beta.1"),
                $"https://downloads.example.test/beta/3.0.0-beta.1",
                "AllEnrolled",
                null,
                now,
                Guid.NewGuid())
        };
        dbContext.ReleaseRings.AddRange(rings);
        dbContext.SaveChanges();
        return rings;
    }

    private static ReleaseRing SeedRing(DbContextOptions<PlanCopeDbContext> options)
    {
        using var dbContext = CreateDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var ring = new ReleaseRing(
            Guid.NewGuid(),
            "1.0.0",
            "stable",
            Sha256Of("1.0.0"),
            "https://downloads.example.test/stable/1.0.0",
            "AllEnrolled",
            null,
            now,
            Guid.NewGuid());
        dbContext.ReleaseRings.Add(ring);
        dbContext.SaveChanges();
        return ring;
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

    private static ReleaseRingsAdminController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal)
    {
        return new ReleaseRingsAdminController(dbContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static ClaimsPrincipal AdminPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("roster_scope", "school"),
            new Claim(ClaimTypes.NameIdentifier, AdminUserId));
    }

    private static ClaimsPrincipal AdminWithoutNameIdentifierPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("roster_scope", "school"));
    }

    // A province-scope roster reader: roster_scope == "province" used to satisfy this
    // controller's gate, which was an authority-widening bug — release rings are Admin-only.
    private static ClaimsPrincipal ProvincePrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "RosterProvince"),
            new Claim("roster_scope", "province"),
            new Claim(ClaimTypes.NameIdentifier, AdminUserId));
    }

    // A plain school-scope viewer with no unbounded scope: every endpoint must Forbid() it —
    // there is no CUE-scoped fallback path on this surface.
    private static ClaimsPrincipal ViewerPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Viewer"),
            new Claim("roster_scope", "school"));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string Sha256Of(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

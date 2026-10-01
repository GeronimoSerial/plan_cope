using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RosterSyncUniversalTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Universal_node_roster_index_returns_all_schools_and_latest_roster_pairs()
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("universal", string.Empty));
        db.Schools.AddRange(School("school-1", 1000001, 0, "Uno"), School("school-2", 1000002, 1, "Dos"));
        db.GeRosterSnapshots.AddRange(
            Snapshot("old", "100000100", "2025", DateTimeOffset.Parse("2025-02-01T00:00:00Z")),
            Snapshot("new", "100000100", "2025", DateTimeOffset.Parse("2025-03-01T00:00:00Z")),
            Snapshot("other-year", "100000200", "2026", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await db.SaveChangesAsync();
        var controller = Controller(db, isNode: true, "universal");

        var result = await controller.GetRosterIndex();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(2, json.RootElement.GetProperty("schools").GetArrayLength());
        var rosters = json.RootElement.GetProperty("rosters").EnumerateArray().ToArray();
        Assert.Equal(2, rosters.Length);
        Assert.Equal("100000100", rosters[0].GetProperty("cue").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("schools").EnumerateArray().Count());
    }

    [Fact]
    public async Task Roster_index_rejects_non_node_access_token()
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("universal", string.Empty));
        await db.SaveChangesAsync();
        var controller = Controller(db, isNode: false, "universal");

        var result = await controller.GetRosterIndex();

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Revoked_universal_node_is_rejected_by_both_roster_endpoints()
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("revoked", string.Empty) with { RevokedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var controller = Controller(db, isNode: true, "revoked");

        Assert.IsType<ForbidResult>((await controller.GetRosterIndex()).Result);
        Assert.IsType<ForbidResult>((await controller.GetRoster("100000100", "2025")).Result);
    }

    [Fact]
    public async Task Scoped_node_roster_index_contains_all_schools_and_rosters()
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("scoped", "100000100"));
        db.Schools.AddRange(School("school-1", 1000001, 0, "Uno"), School("school-2", 1000002, 0, "Dos"));
        db.GeRosterSnapshots.AddRange(
            Snapshot("one", "100000100", "2025", DateTimeOffset.UtcNow),
            Snapshot("two", "100000200", "2025", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var controller = Controller(db, isNode: true, "scoped");

        var ok = Assert.IsType<OkObjectResult>((await controller.GetRosterIndex()).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(new[] { "100000100", "100000200" }, json.RootElement.GetProperty("schools").EnumerateArray()
            .Select(school => school.GetProperty("cue").GetString()));
        Assert.Equal(new[] { "100000100", "100000200" }, json.RootElement.GetProperty("rosters").EnumerateArray()
            .Select(roster => roster.GetProperty("cue").GetString()));
    }

    [Fact]
    public async Task Scoped_node_can_fetch_a_roster_for_another_school()
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("scoped", "100000100"));
        await db.SaveChangesAsync();
        var fetchedAt = DateTimeOffset.UtcNow;
        var emptyPackage = new GeRosterPackageDto("other-school", "100000200", "2025", fetchedAt,
            string.Empty, 0, 0, "Synced", []);
        var otherSchoolRoster = new GeRosterSnapshot
        {
            Id = emptyPackage.SnapshotId,
            Cue = emptyPackage.Cue,
            SchoolYear = emptyPackage.SchoolYear,
            FetchedAt = fetchedAt,
            Checksum = GeRosterPackageChecksum.Calculate(emptyPackage),
            SectionCount = 0,
            StudentCount = 0,
            Status = emptyPackage.Status
        };
        var controller = Controller(db, isNode: true, "scoped", new FakeRosterService(otherSchoolRoster));

        var result = await controller.GetRoster("100000200", "2025");

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(180000001, 1)]
    [InlineData(1800000, 1)]
    public async Task Roster_index_normalizes_full_and_legacy_school_cue_storage(long storedCue, int annex)
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("universal", string.Empty));
        db.Schools.Add(School("prod-shaped", storedCue, annex, "Escuela"));
        await db.SaveChangesAsync();
        var controller = Controller(db, isNode: true, "universal");

        var ok = Assert.IsType<OkObjectResult>((await controller.GetRosterIndex()).Result);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("180000001", json.RootElement.GetProperty("schools")[0].GetProperty("cue").GetString());
    }

    [Theory]
    [InlineData(180000001, 1)]
    [InlineData(1800000, 1)]
    public async Task Roster_package_name_lookup_matches_full_and_legacy_school_rows(long storedCue, int annex)
    {
        using var db = CreateDatabase();
        db.RegisteredNodes.Add(Node("universal", string.Empty));
        db.Schools.Add(School("prod-shaped", storedCue, annex, "Escuela de prueba"));
        await db.SaveChangesAsync();
        const string cue = "180000001";
        var package = new GeRosterPackageDto("snapshot-1", cue, "2026", DateTimeOffset.UtcNow,
            string.Empty, 0, 0, "Synced", []);
        package = package with { Checksum = GeRosterPackageChecksum.Calculate(package) };
        var snapshot = new GeRosterSnapshot
        {
            Id = package.SnapshotId,
            Cue = cue,
            SchoolYear = package.SchoolYear,
            FetchedAt = package.FetchedAt,
            Checksum = package.Checksum,
            SectionCount = 0,
            StudentCount = 0,
            Status = package.Status
        };
        var controller = Controller(db, isNode: true, "universal", new FakeRosterService(snapshot));

        var ok = Assert.IsType<OkObjectResult>((await controller.GetRoster(cue, "2026")).Result);

        Assert.Equal("Escuela de prueba", Assert.IsType<GeRosterPackageDto>(ok.Value).SchoolName);
    }

    private static PlanCopeDbContext CreateDatabase() => new(new DbContextOptionsBuilder<PlanCopeDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .UseInternalServiceProvider(InMemoryServices).Options);

    private static RosterSyncController Controller(PlanCopeDbContext db, bool isNode, string nodeId, IGeRosterService? rosterService = null) =>
        new(db, rosterService!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(NodeAccessAuth.TokenTypeClaim, isNode ? NodeAccessAuth.NodeAccessTokenType : "user_access"),
                        new Claim(NodeAccessAuth.NodeIdClaim, nodeId)
                    }, "test"))
                }
            }
        };

    private static RegisteredNode Node(string id, string cue) => new(id, null, id, null, "Active", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "fp", JsonDocument.Parse("{}"), cue, null,
        DateTimeOffset.UtcNow, null, null);

    private static School School(string id, long cue, int annex, string name) => new(id, id, cue, annex, name,
        "locality", "Active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static GeRosterSnapshot Snapshot(string id, string cue, string year, DateTimeOffset fetchedAt) => new()
    {
        Id = id,
        Cue = cue,
        SchoolYear = year,
        FetchedAt = fetchedAt,
        Checksum = "checksum",
        Status = "ready"
    };

    private sealed class FakeRosterService(GeRosterSnapshot snapshot) : IGeRosterService
    {
        public Task<GeRosterRefreshResult> RefreshAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<GeRosterSnapshot?>(snapshot);

        public Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeRosterSectionStatus>>([]);
    }
}

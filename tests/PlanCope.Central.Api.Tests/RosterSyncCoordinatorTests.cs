using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RosterSyncCoordinatorTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Bulk_run_continues_after_empty_and_failed_schools_and_deduplicates_canonical_cues()
    {
        var fakeRosterService = new FakeRosterService();
        using var provider = CreateProvider(fakeRosterService, hasAsistenciasConnection: true, db =>
        {
            db.Schools.AddRange(
                School("full", 180000001, 1),
                School("legacy-duplicate", 1800000, 1),
                School("failed", 1800000, 2),
                School("empty", 1800000, 3),
                School("created", 1800000, 4),
                School("invalid", 10_000_000_000, 0));
            db.SaveChanges();
        });
        var coordinator = new RosterSyncCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),
            Configuration(hasAsistenciasConnection: true), NullLogger<RosterSyncCoordinator>.Instance);

        var result = await coordinator.SyncAllAsync();

        Assert.True(result.Enabled);
        Assert.False(result.Busy);
        Assert.Equal(4, result.TotalSchools);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Empty);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.InvalidSchools);
        Assert.Equal(new[] { "180000001", "180000002", "180000003", "180000004" }, fakeRosterService.Cues.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Bulk_sync_is_disabled_without_the_asistencias_connection_string()
    {
        var coordinator = new RosterSyncCoordinator(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Configuration(hasAsistenciasConnection: false), NullLogger<RosterSyncCoordinator>.Instance);

        var result = await coordinator.SyncAllAsync();

        Assert.False(coordinator.IsEnabled);
        Assert.False(result.Enabled);
        Assert.Equal(0, result.TotalSchools);
    }

    [Fact]
    public async Task Bulk_sync_is_disabled_when_ge_api_is_the_selected_source_even_if_asistencias_is_configured()
    {
        using var provider = CreateProvider(new FakeRosterService(), hasAsistenciasConnection: true, _ => { });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Asistencias"] = "Host=fixture;Database=asistencias",
            ["Rosters:Source"] = "GeApi",
            ["Rosters:SchoolYear"] = "2026"
        }).Build();
        var coordinator = new RosterSyncCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<RosterSyncCoordinator>.Instance);

        var result = await coordinator.SyncAllAsync();

        Assert.False(coordinator.IsEnabled);
        Assert.False(result.Enabled);
    }

    [Fact]
    public async Task Missing_snapshot_sync_only_processes_cues_without_a_snapshot_for_the_year()
    {
        var fakeRosterService = new FakeRosterService();
        using var provider = CreateProvider(fakeRosterService, hasAsistenciasConnection: true, db =>
        {
            db.Schools.AddRange(School("present", 1800000, 1), School("missing-a", 1800000, 2), School("missing-b", 1800000, 3));
            db.GeRosterSnapshots.Add(new GeRosterSnapshot
            {
                Id = "existing", Cue = "180000001", SchoolYear = "2026", FetchedAt = DateTimeOffset.UtcNow,
                Checksum = "checksum", SectionCount = 1, StudentCount = 1, Status = "Ready"
            });
            db.SaveChanges();
        });
        var coordinator = new RosterSyncCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),
            Configuration(hasAsistenciasConnection: true), NullLogger<RosterSyncCoordinator>.Instance);

        var result = await coordinator.SyncMissingAsync();

        Assert.Equal(2, result.TotalSchools);
        Assert.DoesNotContain("180000001", fakeRosterService.Cues);
        Assert.Equal(new[] { "180000002", "180000003" }, fakeRosterService.Cues.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_failed_database_save_does_not_prevent_later_school_snapshots_from_persisting()
    {
        var interceptor = new FailFirstRosterSaveInterceptor();
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<PlanCopeDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(InMemoryServices)
            .AddInterceptors(interceptor));
        services.AddScoped<IGeRosterService, SnapshotWritingRosterService>();
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            db.Schools.AddRange(School("first-fails-save", 1800000, 1), School("second-persists", 1800000, 2), School("third-persists", 1800000, 3));
            db.SaveChanges();
        }
        interceptor.FailNextSave();
        var coordinator = new RosterSyncCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),
            Configuration(hasAsistenciasConnection: true), NullLogger<RosterSyncCoordinator>.Instance);

        var result = await coordinator.SyncAllAsync();

        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.Succeeded);
        using var verifyScope = provider.CreateScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<PlanCopeDbContext>().GeRosterSnapshots
            .AsNoTracking().Select(snapshot => snapshot.Cue).ToListAsync();
        Assert.Equal(new[] { "180000002", "180000003" }, persisted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Sync_all_endpoint_requires_the_admin_policy()
    {
        var authorization = typeof(RosterSyncAdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", authorization?.Policy);
    }

    private static ServiceProvider CreateProvider(FakeRosterService rosterService, bool hasAsistenciasConnection, Action<PlanCopeDbContext> seed)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<PlanCopeDbContext>(options => options
            .UseInMemoryDatabase(databaseName)
            .UseInternalServiceProvider(InMemoryServices));
        services.AddScoped<IGeRosterService>(_ => rosterService);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        seed(scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>());
        return provider;
    }

    private static IConfiguration Configuration(bool hasAsistenciasConnection) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Asistencias"] = hasAsistenciasConnection ? "Host=fixture;Database=asistencias" : null,
            ["Rosters:SchoolYear"] = "2026"
        }).Build();

    private static School School(string id, long cue, int annex) => new(id, id, cue, annex, id,
        "locality", "Active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeRosterService : IGeRosterService
    {
        public List<string> Cues { get; } = [];

        public Task<GeRosterRefreshResult> RefreshAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            Cues.Add(cue);
            if (cue == "180000002") throw new InvalidOperationException("fixture failure");
            if (cue == "180000003") throw new GeRosterEmptyException(cue, schoolYear);
            return Task.FromResult(new GeRosterRefreshResult("snapshot", cue, schoolYear, DateTimeOffset.UtcNow,
                "checksum", 1, 1, "Ready", true));
        }

        public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<GeRosterSnapshot?>(null);

        public Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeRosterSectionStatus>>([]);
    }

    private sealed class SnapshotWritingRosterService(PlanCopeDbContext db) : IGeRosterService
    {
        public async Task<GeRosterRefreshResult> RefreshAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            var snapshot = new GeRosterSnapshot
            {
                Id = Guid.NewGuid().ToString("N"), Cue = cue, SchoolYear = schoolYear, FetchedAt = DateTimeOffset.UtcNow,
                Checksum = $"checksum-{cue}", SectionCount = 1, StudentCount = 1, Status = "Ready"
            };
            db.GeRosterSnapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);
            return new GeRosterRefreshResult(snapshot.Id, cue, schoolYear, snapshot.FetchedAt, snapshot.Checksum, 1, 1, "Ready", true);
        }

        public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<GeRosterSnapshot?>(null);

        public Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeRosterSectionStatus>>([]);
    }

    private sealed class FailFirstRosterSaveInterceptor : SaveChangesInterceptor
    {
        private int _failNext;

        public void FailNextSave() => Volatile.Write(ref _failNext, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _failNext, 0) == 1)
                throw new DbUpdateException("Synthetic first-school persistence failure.");
            return ValueTask.FromResult(result);
        }
    }
}

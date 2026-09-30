using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
}

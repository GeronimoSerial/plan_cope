using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class ActivationRevalidationServiceTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-revalidation-{Guid.NewGuid():N}.db");
    private readonly string assetsPath = Path.Combine(Path.GetTempPath(), $"plancope-assets-{Guid.NewGuid():N}");
    private readonly string connectionString;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly NodeIdentityRepository identities;
    private readonly SyncStateRepository state;

    public ActivationRevalidationServiceTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        var options = new LocalDatabaseOptions(connectionString);
        new LocalDatabaseInitializer(options).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(options);
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        identities = new NodeIdentityRepository(connectionFactory);
        state = new SyncStateRepository(connectionFactory);
    }

    [Fact]
    public async Task Expiry_wipes_school_and_exam_cache_but_preserves_pending_outbox_results()
    {
        await identities.UpsertAsync(new NodeIdentity("local-node", "node-1", null, "fp", "{}",
            DateTimeOffset.UtcNow.AddDays(-31).ToString("O"), null, "active", null, null));
        await state.UpsertAsync(new SyncState("server-time", "last_server_time",
            System.Text.Json.JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddDays(-31)), DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState("validated-at", "last_revalidation_at",
            System.Text.Json.JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddDays(-31)), DateTimeOffset.UtcNow.ToString("O")));

        using (var connection = connectionFactory.CreateOpenConnection())
        {
            await connection.ExecuteAsync("INSERT INTO schools (cue, name) VALUES ('123456789', 'Escuela');");
            await connection.ExecuteAsync("INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at) VALUES ('exam-1', 'remote-1', 'E1', 1, 'sum', '{}', 1, @Now);", new { Now = DateTimeOffset.UtcNow.ToString("O") });
            await connection.ExecuteAsync("INSERT INTO sync_outbox (id, event_type, aggregate_type, aggregate_id, idempotency_key, payload_json, status, retry_count, next_retry_at, last_error, created_at, processed_at) VALUES ('result-1', 'attempt.submitted', 'attempt', 'a1', 'idem-1', '{\"score\":7}', 'pending', 0, NULL, NULL, @Now, NULL);", new { Now = DateTimeOffset.UtcNow.ToString("O") });
        }

        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var refresher = new NodeCredentialRefresher(services.GetRequiredService<IHttpClientFactory>(), state, identities);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Local:AssetsPath"] = assetsPath
        }).Build();
        var assetService = new LocalAssetFileService(configuration, new LocalExamRepository(connectionFactory));
        var service = new ActivationRevalidationService(identities, state, connectionFactory, refresher,
            assetService, NullLogger<ActivationRevalidationService>.Instance);

        await service.CheckAsync();

        Assert.Null(await identities.GetAsync());
        Assert.True(await service.IsExpiredAsync());
        using var verify = connectionFactory.CreateOpenConnection();
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schools;"));
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_exam_versions;"));
        Assert.Equal(1, await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sync_outbox WHERE id='result-1';"));
        Assert.Equal("{\"score\":7}", await verify.ExecuteScalarAsync<string>("SELECT payload_json FROM sync_outbox WHERE id='result-1';"));
    }

    [Fact]
    public async Task Revalidation_warning_starts_with_five_days_remaining()
    {
        var validatedAt = DateTimeOffset.UtcNow.AddDays(-26);
        await identities.UpsertAsync(new NodeIdentity("local-node", "node-1", null, "fp", "{}",
            validatedAt.ToString("O"), null, "active", null, null));
        await state.UpsertAsync(new SyncState("server-time", "last_server_time",
            System.Text.Json.JsonSerializer.Serialize(validatedAt), DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState("validated-at", "last_revalidation_at",
            System.Text.Json.JsonSerializer.Serialize(validatedAt), DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState("period", "revalidation_interval_days", "\"30\"", DateTimeOffset.UtcNow.ToString("O")));

        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var refresher = new NodeCredentialRefresher(services.GetRequiredService<IHttpClientFactory>(), state, identities);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Local:AssetsPath"] = assetsPath
        }).Build();
        var service = new ActivationRevalidationService(identities, state, connectionFactory, refresher,
            new LocalAssetFileService(configuration, new LocalExamRepository(connectionFactory)),
            NullLogger<ActivationRevalidationService>.Instance);

        await service.CheckAsync();

        Assert.InRange(await service.GetDaysRemainingAsync() ?? -1, 1, 5);
        Assert.False(await service.IsExpiredAsync());
        Assert.NotNull(await identities.GetAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        if (Directory.Exists(assetsPath)) Directory.Delete(assetsPath, recursive: true);
    }
}

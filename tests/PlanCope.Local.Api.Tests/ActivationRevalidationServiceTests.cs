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
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly NodeIdentityRepository identities;
    private readonly SyncStateRepository state;

    public ActivationRevalidationServiceTests()
    {
        var options = new LocalDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        new LocalDatabaseInitializer(options).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(options);
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        identities = new NodeIdentityRepository(connectionFactory);
        state = new SyncStateRepository(connectionFactory);
    }

    [Fact]
    public async Task Expiry_wipes_fk_graph_but_preserves_pending_outbox_results()
    {
        await SeedExpiredIdentityAsync(DateTimeOffset.UtcNow.AddDays(-31));
        await SeedWipeGraphAsync(activeSession: false, unsubmittedAttempt: false);
        var service = CreateService(TimeProvider.System);

        await service.CheckAsync();

        Assert.Null(await identities.GetAsync());
        Assert.True(await service.IsExpiredAsync());
        using var verify = connectionFactory.CreateOpenConnection();
        foreach (var table in new[] { "schools", "local_exam_versions", "local_exam_blocks", "delivery_sessions", "student_attempts", "submission_answers", "attempt_results", "stats_rollups", "stats_rollup_blocks" })
            Assert.Equal(0, await verify.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table};"));
        Assert.Equal(1, await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sync_outbox WHERE id='result-1';"));
        Assert.Equal("{\"score\":7}", await verify.ExecuteScalarAsync<string>("SELECT payload_json FROM sync_outbox WHERE id='result-1';"));
        Assert.Equal(0, await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task Revalidation_warning_starts_with_five_days_remaining()
    {
        var validatedAt = DateTimeOffset.UtcNow.AddDays(-26);
        await SeedExpiredIdentityAsync(validatedAt);
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "revalidation_interval_days", "\"30\"", DateTimeOffset.UtcNow.ToString("O")));
        var service = CreateService(TimeProvider.System);

        await service.CheckAsync();

        Assert.InRange(await service.GetDaysRemainingAsync() ?? -1, 1, 5);
        Assert.False(await service.IsExpiredAsync());
        Assert.NotNull(await identities.GetAsync());
    }

    [Fact]
    public async Task Expiry_defers_wipe_while_session_or_attempt_is_unfinished_then_wipes()
    {
        await SeedExpiredIdentityAsync(DateTimeOffset.UtcNow.AddDays(-31));
        await SeedWipeGraphAsync(activeSession: true, unsubmittedAttempt: true);
        var service = CreateService(TimeProvider.System);

        await service.CheckAsync();

        Assert.NotNull(await identities.GetAsync());
        Assert.False(await service.IsExpiredAsync());
        Assert.True(await service.IsExpiryPendingAsync());
        using (var connection = connectionFactory.CreateOpenConnection())
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_exam_versions;"));

        using (var connection = connectionFactory.CreateOpenConnection())
            await connection.ExecuteAsync("UPDATE delivery_sessions SET status='closed'; UPDATE student_attempts SET submitted_at=@Now, status='submitted';", new { Now = DateTimeOffset.UtcNow.ToString("O") });

        await service.CheckAsync();

        Assert.Null(await identities.GetAsync());
        Assert.True(await service.IsExpiredAsync());
    }

    [Fact]
    public async Task Future_local_clock_only_warns_and_recovers_when_corrected()
    {
        var validatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        await SeedExpiredIdentityAsync(validatedAt);
        var clock = new MutableTimeProvider(validatedAt.AddDays(365));
        var service = CreateService(clock);

        await service.CheckAsync();

        Assert.NotNull(await identities.GetAsync());
        Assert.False(await service.IsExpiredAsync());
        Assert.False(await service.IsExpiryPendingAsync());
        Assert.Equal(0, await service.GetDaysRemainingAsync());

        clock.UtcNow = validatedAt.AddDays(10);
        await service.CheckAsync();

        Assert.NotNull(await identities.GetAsync());
        Assert.False(await service.IsExpiredAsync());
        Assert.InRange(await service.GetDaysRemainingAsync() ?? -1, 19, 20);
    }

    [Fact]
    public async Task Expiry_asset_cleanup_resumes_after_a_crash()
    {
        Directory.CreateDirectory(assetsPath);
        var staleAsset = Path.Combine(assetsPath, "stale.bin");
        await File.WriteAllTextAsync(staleAsset, "old exam asset");
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "activation_expired", "true", DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "activation_assets_cleanup_pending", "true", DateTimeOffset.UtcNow.ToString("O")));
        var service = CreateService(TimeProvider.System);

        await service.CheckAsync();

        Assert.False(File.Exists(staleAsset));
        Assert.False(await ReadAssetCleanupPendingForTestAsync());
    }

    private async Task SeedExpiredIdentityAsync(DateTimeOffset validatedAt)
    {
        await identities.UpsertAsync(new NodeIdentity("local-node", "node-1", null, "fp", "{}",
            validatedAt.ToString("O"), null, "active", null, null));
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "last_server_time",
            System.Text.Json.JsonSerializer.Serialize(validatedAt), DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "last_revalidation_at",
            System.Text.Json.JsonSerializer.Serialize(validatedAt), DateTimeOffset.UtcNow.ToString("O")));
        await state.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "revalidation_interval_days", "30", DateTimeOffset.UtcNow.ToString("O")));
    }

    private async Task SeedWipeGraphAsync(bool activeSession, bool unsubmittedAttempt)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync("""
            INSERT INTO schools (cue, name) VALUES ('123456789', 'Escuela');
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ('exam-1', 'remote-1', 'E1', 1, 'sum', '{}', 1, @Now);
            INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json)
                VALUES ('block-1', 'exam-1', 'remote-block-1', 0, 'multiple_choice', '{}');
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, started_by, start_at, status)
                VALUES ('session-1', 'exam-1', '123456789', 'teacher', @Now, @SessionStatus);
            INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence)
                VALUES ('attempt-1', 'session-1', 'student-1', @AttemptStatus, @Now, @SubmittedAt, 1);
            INSERT INTO submission_answers (id, student_attempt_id, block_id, answer_json, created_at)
                VALUES ('answer-1', 'attempt-1', 'block-1', '{}', @Now);
            INSERT INTO attempt_results (id, student_attempt_id, grading_schema_version, status, score, score_max, blocks_json, graded_at)
                VALUES ('grade-1', 'attempt-1', 1, 'graded', 7, 10, '{}', @Now);
            INSERT INTO stats_rollups (id, cue, school_year, course, exam_version_id, attempt_count, score_sum, score_max_sum, updated_at)
                VALUES ('rollup-1', '123456789', '2026', '1', 'exam-1', 1, 7, 10, @Now);
            INSERT INTO stats_rollup_blocks (id, rollup_id, block_id, correct_count, partial_count, incorrect_count, blank_count, ungradable_count, score_sum, score_max_sum)
                VALUES ('rollup-block-1', 'rollup-1', 'block-1', 1, 0, 0, 0, 0, 7, 10);
            INSERT INTO sync_outbox (id, event_type, aggregate_type, aggregate_id, idempotency_key, payload_json, status, retry_count, next_retry_at, last_error, created_at, processed_at)
                VALUES ('result-1', 'attempt.submitted', 'attempt', 'attempt-1', 'idem-1', '{"score":7}', 'pending', 0, NULL, NULL, @Now, NULL);
            """, new
        {
            Now = now,
            SessionStatus = activeSession ? "active" : "closed",
            AttemptStatus = unsubmittedAttempt ? "in_progress" : "submitted",
            SubmittedAt = unsubmittedAttempt ? null : now
        });
    }

    private ActivationRevalidationService CreateService(TimeProvider clock)
    {
        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var refresher = new NodeCredentialRefresher(services.GetRequiredService<IHttpClientFactory>(), state, identities);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Local:AssetsPath"] = assetsPath
        }).Build();
        var assetService = new LocalAssetFileService(configuration, new LocalExamRepository(connectionFactory));
        return new ActivationRevalidationService(identities, state, connectionFactory, refresher,
            assetService, NullLogger<ActivationRevalidationService>.Instance, clock);
    }

    private async Task<bool> ReadAssetCleanupPendingForTestAsync()
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var value = await connection.ExecuteScalarAsync<string>("SELECT value_json FROM sync_state WHERE key='activation_assets_cleanup_pending';");
        return value == "true";
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        if (Directory.Exists(assetsPath)) Directory.Delete(assetsPath, recursive: true);
    }
}

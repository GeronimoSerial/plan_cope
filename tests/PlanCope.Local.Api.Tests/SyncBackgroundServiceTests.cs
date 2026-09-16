using System.Net;
using System.Text;
using System.Text.Json;
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

public sealed class SyncBackgroundServiceTests : IDisposable
{
    private const string CentralUrl = "https://central.test";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-sync-{Guid.NewGuid():N}.db");
    private readonly string connectionString;
    private readonly LocalSqliteConnectionFactory connectionFactory;

    public SyncBackgroundServiceTests()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
    }

    [Fact]
    public async Task Never_calls_central_while_a_session_is_active()
    {
        await SeedActiveSessionAsync();
        await SeedSyncStateAsync("central_url", CentralUrl);

        var handler = new ThrowingHandler();
        var service = BuildService(new StubHttpClientFactory(handler));

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.Null(handler.CapturedException);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Probe_success_writes_operator_visible_sync_state()
    {
        await SeedSyncStateAsync("central_url", CentralUrl);
        var startedAt = DateTimeOffset.UtcNow;

        var handler = new HealthOnlyHandler();
        var service = BuildService(new StubHttpClientFactory(handler));

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.Null(handler.CapturedException);
        Assert.True(handler.CallCount >= 1);

        var syncStateRepository = new SyncStateRepository(connectionFactory);

        var nextAttempt = await syncStateRepository.GetAsync("sync_next_attempt_at");
        Assert.NotNull(nextAttempt);
        Assert.False(string.IsNullOrWhiteSpace(nextAttempt!.ValueJson));
        var nextAttemptAt = JsonSerializer.Deserialize<DateTimeOffset>(nextAttempt.ValueJson);
        Assert.True(nextAttemptAt > startedAt);

        var lastError = await syncStateRepository.GetAsync("sync_last_error");
        Assert.NotNull(lastError);
    }

    private SyncBackgroundService BuildService(IHttpClientFactory httpClientFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ILocalSqliteConnectionFactory>(connectionFactory);
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<ILocalExamRepository, LocalExamRepository>();
        services.AddScoped<LocalAssetFileService>();
        services.AddScoped<LocalExamPullService>();
        services.AddScoped<LocalOutboxPushService>();
        services.AddSingleton(httpClientFactory);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new SyncBackgroundService(
            scopeFactory,
            httpClientFactory,
            NullLogger<SyncBackgroundService>.Instance);
    }

    private async Task SeedActiveSessionAsync()
    {
        using (var connection = connectionFactory.CreateOpenConnection())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, schema_version, synced_at)
                VALUES ('exam-version-1', 'remote-exam-version-1', 'EXAM-1', 1, 'checksum-1', 1, '2026-09-15T00:00:00Z');
                """));
        }

        await new SessionRepository(connectionFactory).CreateAsync(new LocalDeliverySession(
            Id: "session-active",
            ExamVersionId: "exam-version-1",
            SchoolCode: "180055400",
            ClassroomCode: null,
            CommissionCode: null,
            StartedBy: "user-1",
            StartAt: "2026-09-15T08:00:00Z",
            EndAt: null,
            Status: "active",
            ConfigJson: null,
            AccessCode: "ACTIVE1",
            ExpectedStudentCount: 0,
            SchoolYear: "2026",
            RosterSnapshotId: null,
            RosterSectionId: null));
    }

    private Task SeedSyncStateAsync(string key, string value)
        => new SyncStateRepository(connectionFactory).UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            JsonSerializer.Serialize(value),
            DateTimeOffset.UtcNow.ToString("O")));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Exception? CapturedException { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            CapturedException = new InvalidOperationException("no HTTP call should happen during an active session");
            throw CapturedException;
        }
    }

    private sealed class HealthOnlyHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Exception? CapturedException { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.RequestUri?.AbsolutePath == "/health/live")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json")
                });
            }

            CapturedException = new InvalidOperationException($"unexpected HTTP call to {request.RequestUri}");
            throw CapturedException;
        }
    }
}

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
    public async Task Active_session_sends_small_heartbeat_without_adding_outbox_items()
    {
        await SeedActiveSessionAsync();
        using (var insertConnection = connectionFactory.CreateOpenConnection())
        {
            await insertConnection.ExecuteAsync("""
                INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code, submission_reason)
                VALUES ('attempt-in-progress', 'session-active', 'student-1', 'in_progress', @StartedAt, NULL, 1, NULL, NULL),
                       ('attempt-submitted', 'session-active', 'student-2', 'submitted', @StartedAt, @SubmittedAt, 2, 'AB12CD34', 'session_closed');
                """, new { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3).ToString("O"), SubmittedAt = DateTimeOffset.UtcNow.ToString("O") });
        }
        await SeedSyncStateAsync("central_url", CentralUrl);
        await new NodeIdentityRepository(connectionFactory).UpsertAsync(new NodeIdentity(
            "identity-1", "node-1", "180055400", "fingerprint", "{}", "2026-09-15T00:00:00Z", null,
            "active", null, null));
        Assert.Equal("node-1", (await new NodeIdentityRepository(connectionFactory).GetAsync())?.NodeId);
        Assert.NotNull(await new SessionRepository(connectionFactory).GetHeartbeatSnapshotAsync("session-active"));

        var handler = new HeartbeatHandler();
        var clientFactory = new StubHttpClientFactory(handler);
        var service = BuildService(clientFactory);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(handler.Paths, path => path == "/health/live");
        var heartbeat = Assert.Single(handler.HeartbeatBodies);
        Assert.True(Encoding.UTF8.GetByteCount(heartbeat) < 2048);
        Assert.Contains("\"sessionId\":\"session-active\"", heartbeat);
        Assert.Contains("\"joinedCount\":2", heartbeat);
        Assert.Contains("\"inProgressCount\":1", heartbeat);
        Assert.Contains("\"submittedCount\":1", heartbeat);
        Assert.Contains("\"closedOrForcedCount\":1", heartbeat);
        using var connection = connectionFactory.CreateOpenConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sync_outbox;"));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/api/sync/push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Heartbeat_is_skipped_when_health_probe_fails()
    {
        await SeedActiveSessionAsync();
        await SeedSyncStateAsync("central_url", CentralUrl);
        await new NodeIdentityRepository(connectionFactory).UpsertAsync(new NodeIdentity(
            "identity-1", "node-1", "180055400", "fingerprint", "{}", "2026-09-15T00:00:00Z", null,
            "active", null, null));
        var handler = new HealthFailingHandler();
        var service = BuildService(new StubHttpClientFactory(handler));
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);
        Assert.DoesNotContain(handler.Paths, path => path == "/api/sync/session-heartbeat");
    }

    [Fact]
    public async Task Heartbeat_is_skipped_when_health_probe_is_slow()
    {
        await SeedActiveSessionAsync();
        await SeedSyncStateAsync("central_url", CentralUrl);
        await new NodeIdentityRepository(connectionFactory).UpsertAsync(new NodeIdentity(
            "identity-1", "node-1", "180055400", "fingerprint", "{}", "2026-09-15T00:00:00Z", null,
            "active", null, null));
        var handler = new SlowHealthHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SessionHeartbeat:HealthTimeoutSeconds"] = "1"
        }).Build();
        var service = BuildService(new StubHttpClientFactory(handler), config);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1400);
        await service.StopAsync(CancellationToken.None);
        Assert.DoesNotContain(handler.Paths, path => path == "/api/sync/session-heartbeat");
    }

    [Fact]
    public async Task Heartbeat_request_is_awaited_by_background_worker()
    {
        await SeedActiveSessionAsync();
        await SeedSyncStateAsync("central_url", CentralUrl);
        await new NodeIdentityRepository(connectionFactory).UpsertAsync(new NodeIdentity(
            "identity-1", "node-1", "180055400", "fingerprint", "{}", "2026-09-15T00:00:00Z", null,
            "active", null, null));
        var handler = new InFlightHeartbeatHandler();
        var service = BuildService(new StubHttpClientFactory(handler));
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(400);
        Assert.Equal(1, handler.HeartbeatCount);
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(1, handler.HeartbeatCount);
    }

    [Fact]
    public async Task Rejected_heartbeat_is_not_retried_with_a_refreshed_token()
    {
        await SeedSyncStateAsync("central_access_token", "access-token");
        var handler = new UnauthorizedHandler();
        using var client = new HttpClient(new SessionHeartbeatCredentialHandler(new SyncStateRepository(connectionFactory))
        {
            InnerHandler = handler
        });
        using var response = await client.PostAsync("https://central.test/api/sync/session-heartbeat", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("access-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task Stale_open_session_no_longer_blocks_normal_sync()
    {
        await SeedActiveSessionAsync();
        using (var connection = connectionFactory.CreateOpenConnection())
        {
            await connection.ExecuteAsync("UPDATE delivery_sessions SET start_at = @StartedAt WHERE id = 'session-active';",
                new { StartedAt = DateTimeOffset.UtcNow.AddHours(-3).ToString("O") });
        }
        Assert.True((await new SessionRepository(connectionFactory).GetActiveLastActivityAsync())[
            "session-active"] < DateTimeOffset.UtcNow.AddHours(-2));
        await SeedSyncStateAsync("central_url", CentralUrl);
        await SeedSyncStateAsync("node_id", "node-1");
        var handler = new SyncPullHandler();
        var service = BuildService(new StubHttpClientFactory(handler));
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);
        Assert.Contains(handler.Paths, path => path.StartsWith("/api/sync/pull", StringComparison.Ordinal));
        var session = await new SessionRepository(connectionFactory).GetByIdAsync("session-active");
        Assert.Equal("active", session?.Status);
    }

    [Fact]
    public async Task Active_activity_query_ignores_attempt_history_for_closed_sessions()
    {
        await SeedActiveSessionAsync();
        await new SessionRepository(connectionFactory).CreateAsync(new LocalDeliverySession(
            Id: "session-closed",
            ExamVersionId: "exam-version-1",
            SchoolCode: "180055400",
            ClassroomCode: null,
            CommissionCode: null,
            StartedBy: "user-1",
            StartAt: DateTimeOffset.UtcNow.AddHours(-4).ToString("O"),
            EndAt: DateTimeOffset.UtcNow.AddHours(-3).ToString("O"),
            Status: "closed",
            ConfigJson: null,
            AccessCode: "CLOSED1",
            ExpectedStudentCount: 1,
            SchoolYear: "2026",
            RosterSnapshotId: null,
            RosterSectionId: null));
        using (var connection = connectionFactory.CreateOpenConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code, submission_reason)
                VALUES ('closed-attempt', 'session-closed', 'student-closed', 'submitted', @StartedAt, @SubmittedAt, 1, 'AB12CD34', 'session_closed');
                """, new { StartedAt = DateTimeOffset.UtcNow.AddHours(-4).ToString("O"), SubmittedAt = DateTimeOffset.UtcNow.ToString("O") });
        }

        var activity = await new SessionRepository(connectionFactory).GetActiveLastActivityAsync();

        Assert.Contains("session-active", activity.Keys);
        Assert.DoesNotContain("session-closed", activity.Keys);
        Assert.True(activity["session-active"] < DateTimeOffset.UtcNow.AddMinutes(-5));
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

    private SyncBackgroundService BuildService(IHttpClientFactory httpClientFactory, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddSingleton<ILocalSqliteConnectionFactory>(connectionFactory);
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<INodeIdentityRepository, NodeIdentityRepository>();
        services.AddScoped<INodeIdentityRepository, NodeIdentityRepository>();
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
            NullLogger<SyncBackgroundService>.Instance,
            configuration ?? new ConfigurationBuilder().Build());
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
            StartAt: DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"),
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
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
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

    private sealed class HeartbeatHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> HeartbeatBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Paths.Add(path);
            if (path == "/api/sync/session-heartbeat")
                HeartbeatBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class HealthFailingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(path == "/health/live" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        }
    }

    private sealed class SyncPullHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[],\"nextCursor\":\"0\",\"hasMore\":false,\"checksums\":{}}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SlowHealthHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Paths.Add(path);
            if (path == "/health/live") await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class InFlightHeartbeatHandler : HttpMessageHandler
    {
        public int HeartbeatCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/sync/session-heartbeat")
            {
                HeartbeatCount++;
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}

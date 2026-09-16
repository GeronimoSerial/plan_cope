using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class RevocationEnforcerTests : IDisposable
{
    private const string Cue = "180055400";
    private const string CentralUrl = "https://central.test";
    private const string EmptyJsonString = "\"\"";

    private static readonly string[] CredentialKeys =
    [
        "central_access_token",
        "central_refresh_token",
        "central_access_token_expires_at",
        "central_refresh_token_expires_at",
        "node_id",
    ];

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-revocation-{Guid.NewGuid():N}.db");
    private readonly string connectionString;
    private readonly LocalSqliteConnectionFactory connectionFactory;

    public RevocationEnforcerTests()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
    }

    [Fact]
    public async Task No_node_identity_returns_NotApplicable_and_touches_nothing()
    {
        await SeedSchoolAsync();
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedRosterAsync();
        var before = await CaptureAsync();
        var handler = new ThrowingHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.NotApplicable, outcome);
        Assert.Equal(before, await CaptureAsync());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Active_credential_state_returns_NotApplicable_and_touches_nothing()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("active", null);
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedRosterAsync();
        var before = await CaptureAsync();
        var handler = new ThrowingHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.NotApplicable, outcome);
        Assert.Equal(before, await CaptureAsync());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Already_locked_returns_AlreadyLocked_and_touches_nothing()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("revoked", "locked");
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedRosterAsync();
        var before = await CaptureAsync();
        var handler = new ThrowingHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.AlreadyLocked, outcome);
        Assert.Equal(before, await CaptureAsync());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task WaitingForSessionEnd_never_destroys_data_mid_session()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("revoked", null);
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedRosterAsync();
        await SeedActiveSessionAsync();
        var before = await CaptureAsync();
        var handler = new ThrowingHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.WaitingForSessionEnd, outcome);
        Assert.Equal(before, await CaptureAsync());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DrainIncomplete_keeps_roster_and_credentials_intact()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("revoked", null);
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedOutboxAsync("outbox-2", "idempotency-2");
        await SeedOutboxAsync("outbox-3", "idempotency-3");
        await SeedRosterAsync();
        var before = await CaptureAsync();
        var handler = new FailingPushHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.DrainIncomplete, outcome);
        Assert.Equal(before, await CaptureAsync());
        Assert.True(handler.CallCount >= 1);
    }

    [Fact]
    public async Task Successful_drain_wipes_roster_and_credentials_then_locks()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("revoked", null);
        await SeedCredentialStateAsync();
        await SeedOutboxAsync("outbox-1", "idempotency-1");
        await SeedOutboxAsync("outbox-2", "idempotency-2");
        await SeedOutboxAsync("outbox-3", "idempotency-3");
        await SeedRosterAsync();
        var handler = new AcceptingPushHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.Locked, outcome);
        Assert.True(handler.CallCount >= 1);
        Assert.Equal(0, await CountAsync("sync_outbox WHERE status = 'pending'"));
        Assert.Equal(0, await CountAsync("local_roster_snapshots"));
        Assert.Equal(0, await CountAsync("local_roster_sections"));
        Assert.Equal(0, await CountAsync("local_roster_students"));
        foreach (var key in CredentialKeys)
        {
            Assert.Equal(EmptyJsonString, await ReadSyncStateAsync(key));
        }

        Assert.Equal("\"https://central.test\"", await ReadSyncStateAsync("central_url"));

        var identity = await ReadIdentityAsync();
        Assert.NotNull(identity);
        Assert.Equal("locked", identity!.RevocationStage);
        Assert.Equal("revoked", identity.CredentialState);
    }

    [Fact]
    public async Task Resumes_from_wiped_stage_without_re_draining_or_re_wiping()
    {
        await SeedSchoolAsync();
        await SeedIdentityAsync("revoked", "wiped");
        await SeedSyncStateAsync("central_url", CentralUrl);
        foreach (var key in CredentialKeys)
        {
            await SeedSyncStateAsync(key, string.Empty);
        }

        var handler = new ThrowingHandler();

        var outcome = await CreateEnforcer(handler).TryAdvanceAsync();

        Assert.Equal(RevocationEnforcementOutcome.Locked, outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, await CountAsync("local_roster_snapshots"));
        Assert.Equal(0, await CountAsync("local_roster_sections"));
        Assert.Equal(0, await CountAsync("local_roster_students"));

        var identity = await ReadIdentityAsync();
        Assert.NotNull(identity);
        Assert.Equal("locked", identity!.RevocationStage);
        Assert.Equal("revoked", identity.CredentialState);
    }

    private RevocationEnforcer CreateEnforcer(HttpMessageHandler handler)
    {
        var outboxRepository = new OutboxRepository(connectionFactory);
        var pushService = new LocalOutboxPushService(
            new StubHttpClientFactory(handler),
            new SyncStateRepository(connectionFactory),
            outboxRepository);

        return new RevocationEnforcer(
            new NodeIdentityRepository(connectionFactory),
            new SessionRepository(connectionFactory),
            outboxRepository,
            new SyncStateRepository(connectionFactory),
            connectionFactory,
            pushService);
    }

    private async Task SeedSchoolAsync()
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT OR IGNORE INTO schools (cue, name) VALUES (@Cue, NULL);",
            new { Cue }));
    }

    private Task SeedIdentityAsync(string credentialState, string? revocationStage)
        => new NodeIdentityRepository(connectionFactory).UpsertAsync(new NodeIdentity(
            Id: "identity-1",
            NodeId: null,
            Cue: Cue,
            FingerprintHash: "fingerprint-hash",
            FingerprintComponentsJson: "{}",
            EnrolledAt: null,
            LastSyncAt: null,
            CredentialState: credentialState,
            RevocationDetectedAt: null,
            RevocationStage: revocationStage));

    private async Task SeedCredentialStateAsync()
    {
        await SeedSyncStateAsync("central_url", CentralUrl);
        await SeedSyncStateAsync("central_access_token", "original-access-token");
        await SeedSyncStateAsync("central_refresh_token", "original-refresh-token");
        await SeedSyncStateAsync("central_access_token_expires_at", "2030-01-01T00:00:00Z");
        await SeedSyncStateAsync("central_refresh_token_expires_at", "2030-01-01T00:00:00Z");
        await SeedSyncStateAsync("node_id", "node-1");
    }

    private Task SeedSyncStateAsync(string key, string value)
        => new SyncStateRepository(connectionFactory).UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            JsonSerializer.Serialize(value),
            DateTimeOffset.UtcNow.ToString("O")));

    private Task SeedOutboxAsync(string id, string idempotencyKey)
        => new OutboxRepository(connectionFactory).InsertAsync(new SyncOutbox(
            Id: id,
            EventType: SyncEventTypes.SessionCreated,
            AggregateType: "delivery_session",
            AggregateId: id,
            IdempotencyKey: idempotencyKey,
            PayloadJson: """{"id":"aggregate-1"}""",
            Status: "pending",
            RetryCount: 0,
            NextRetryAt: null,
            LastError: null,
            CreatedAt: DateTimeOffset.UtcNow.ToString("O"),
            ProcessedAt: null));

    private async Task SeedRosterAsync()
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
            VALUES ('snapshot-1', @Cue, '2026', @Now, 'checksum-1', 1, 1, 'Ready');
            INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
            VALUES ('section-1', 'snapshot-1', 100, '6º', 'A', 'Primario', 'Mañana');
            INSERT INTO local_roster_students (id, snapshot_id, section_id, ge_person_id, document_hash, document_last4, first_name, last_name)
            VALUES ('student-1', 'snapshot-1', 'section-1', 101, 'document-hash', '5678', 'Ana', 'Pérez');
            """,
            new { Cue, Now = DateTimeOffset.UtcNow.ToString("O") }));
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
            SchoolCode: Cue,
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
            RosterSnapshotId: "snapshot-1",
            RosterSectionId: "section-1"));
    }

    private Task<NodeIdentity?> ReadIdentityAsync() => new NodeIdentityRepository(connectionFactory).GetAsync();

    private async Task<int> CountAsync(string fromClause)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM {fromClause};"));
    }

    private async Task<string?> ReadSyncStateAsync(string key)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value_json FROM sync_state WHERE key = @Key;",
            new { Key = key }));
    }

    private async Task<RevocationSnapshot> CaptureAsync()
    {
        var identity = await ReadIdentityAsync();
        return new RevocationSnapshot(
            OutboxTotal: await CountAsync("sync_outbox"),
            OutboxPending: await CountAsync("sync_outbox WHERE status = 'pending'"),
            RosterSnapshots: await CountAsync("local_roster_snapshots"),
            RosterSections: await CountAsync("local_roster_sections"),
            RosterStudents: await CountAsync("local_roster_students"),
            AccessToken: await ReadSyncStateAsync("central_access_token"),
            RefreshToken: await ReadSyncStateAsync("central_refresh_token"),
            NodeId: await ReadSyncStateAsync("node_id"),
            Stage: identity?.RevocationStage,
            CredentialState: identity?.CredentialState);
    }

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

    private sealed record RevocationSnapshot(
        int OutboxTotal,
        int OutboxPending,
        int RosterSnapshots,
        int RosterSections,
        int RosterStudents,
        string? AccessToken,
        string? RefreshToken,
        string? NodeId,
        string? Stage,
        string? CredentialState);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("Unexpected HTTP call during revocation enforcement.");
        }
    }

    private sealed class FailingPushHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class AcceptingPushHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var push = JsonSerializer.Deserialize<PushRequest>(body, JsonOptions)!;
            var results = push.Items
                .Select(item => new PushItemResult(item.IdempotencyKey, "accepted", null))
                .ToList();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PushResponse(push.Items.Count, 0, results), options: JsonOptions)
            };
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Local.Api;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.RosterCrypto;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.Local;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.E2E.Tests;

public sealed class PublishPullRunPushTests
{
    [Fact]
    public async Task Exam_published_on_central_is_pulled_run_and_pushed_back_to_central()
    {
        using var centralFactory = new CentralApiFactory();
        var accessToken = CreateAccessToken();

        using var centralClient = centralFactory.CreateClient();
        centralClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var exam = await CreateExamOnCentralAsync(centralClient);
        var version = await CreateExamVersionOnCentralAsync(centralClient, exam.Id);
        var block = await AddBlockOnCentralAsync(centralClient, version.Id);
        await PublishVersionOnCentralAsync(centralClient, version.Id);

        using var localFactory = new LocalApiFactory(centralFactory);
        using var localClient = localFactory.CreateClient();
        var health = await localClient.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await SeedSyncStateAsync(localFactory, centralFactory.PlaceholderCentralUrl, accessToken);

        using (var pullScope = localFactory.Services.CreateScope())
        {
            var pull = pullScope.ServiceProvider.GetRequiredService<LocalExamPullService>();
            var result = await pull.PullAsync(CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Imported >= 1, $"expected at least one imported exam version but got {result.Imported}");
        }

        var attemptId = await RunSessionAndSubmitAsync(localClient, version.Id, block.Id);

        using (var pushScope = localFactory.Services.CreateScope())
        {
            var push = pushScope.ServiceProvider.GetRequiredService<LocalOutboxPushService>();
            var result = await push.PushAsync(50, CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Accepted >= 1, $"expected at least one accepted outbox item but got {result.Accepted}");
        }

        using (var centralScope = centralFactory.Services.CreateScope())
        {
            var db = centralScope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            var received = await db.ReceivedStudentAttempts
                .SingleOrDefaultAsync(x => x.RemoteLocalId == attemptId);
            Assert.NotNull(received);
            Assert.Equal("submitted", received.Status);
        }
    }

    [Fact]
    public async Task Full_system_lifecycle_offline_activation_grading_stats_sync_and_gated_update()
    {
        using var centralFactory = new CentralApiFactory();
        var accessToken = CreateAccessToken();
        using var centralClient = centralFactory.CreateClient();
        centralClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // 1. Author + publish on Central (reuse existing helpers verbatim)
        var exam = await CreateExamOnCentralAsync(centralClient);
        var version = await CreateExamVersionOnCentralAsync(centralClient, exam.Id);
        var block = await AddBlockOnCentralAsync(centralClient, version.Id);
        await PublishVersionOnCentralAsync(centralClient, version.Id);

        // 2. Build a REAL encrypted roster bundle on disk, exactly like
        //    tests/PlanCope.Local.Api.Tests/EmbeddedRosterSeederTests.cs's CreatePackage()/setup does
        var bundleDir = Path.Combine(Path.GetTempPath(), $"plancope-e2e-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundleDir);
        var input = Path.Combine(bundleDir, "input");
        Directory.CreateDirectory(input);
        var section = new GeRosterSectionPackageDto("e2e-section", 900001, "1", "A", "Primario", "Mañana",
            [new("e2e-student", "e2e-section", 910001, "99000001", "Ada", "Ejemplo")]);
        var withoutChecksum = new GeRosterPackageDto("e2e-snapshot", "180055400", "2026",
            DateTimeOffset.Parse("2026-01-15T12:00:00Z"), new string('0', 64), 1, 1, "Ready", [section], "Escuela E2E");
        var package = withoutChecksum with { Checksum = GeRosterPackageChecksum.Calculate(withoutChecksum) };
        await File.WriteAllTextAsync(Path.Combine(input, "180055400-2026.roster.json"),
            JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var bundlePath = Path.Combine(bundleDir, "rosters.enc");
        const string passphrase = "e2e-only";
        await EnvelopeEncryption.EncryptDirectoryAsync(input, bundlePath, passphrase, new Argon2Parameters(8, 1, 1));

        // 3. Local factory, with the bundle wired in
        using var localFactory = new LocalApiFactory(centralFactory, bundlePath, passphrase);
        using var localClient = localFactory.CreateClient();
        var health = await localClient.GetAsync("/api/health");
        Assert.True(health.StatusCode == HttpStatusCode.OK, $"health={health.StatusCode} body={await health.Content.ReadAsStringAsync()}");
        await SeedSyncStateAsync(localFactory, centralFactory.PlaceholderCentralUrl, accessToken);

        // 4. Initial catalog pull WHILE CONNECTED (routine sync, before the school goes offline)
        localFactory.Connectivity.Offline = false;
        using (var pullScope = localFactory.Services.CreateScope())
        {
            var pull = pullScope.ServiceProvider.GetRequiredService<LocalExamPullService>();
            var result = await pull.PullAsync(CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Imported >= 1, $"expected at least one imported exam version but got {result.Imported}");
        }

        // 5. Go OFFLINE for everything that follows until step 9 — any Central call in this window throws.
        localFactory.Connectivity.Offline = true;

        // 6. OFFLINE Phase A activation, through the real HTTP endpoints
        var bundleCuesResponse = await localClient.GetAsync("/api/activation/bundle-cues");
        Assert.Equal(HttpStatusCode.OK, bundleCuesResponse.StatusCode);
        using (var bundleCuesDoc = JsonDocument.Parse(await bundleCuesResponse.Content.ReadAsStringAsync()))
        {
            var cues = bundleCuesDoc.RootElement.GetProperty("cues").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("180055400", cues);
        }
        var unlockResponse = await localClient.PostAsJsonAsync("/api/activation/unlock", new { passphrase, cue = "180055400" });
        Assert.True(unlockResponse.StatusCode == HttpStatusCode.OK, await unlockResponse.Content.ReadAsStringAsync());

        // 7. OFFLINE: run a NOMINAL session against the real roster activated in step 6 and submit
        //    an attempt. Rollups are only written for nominal sessions, so step 8's stats assertion
        //    requires SchoolYear/RosterSnapshotId/RosterSectionId to all be set.
        var nominalSessionResponse = await localClient.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            version.Id, "180055400", "6A", null, "Operador", 30, null, "2026", "e2e-snapshot", "e2e-section"));
        Assert.True(nominalSessionResponse.StatusCode == HttpStatusCode.Created, await nominalSessionResponse.Content.ReadAsStringAsync());
        var nominalSession = await nominalSessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(nominalSession);

        var resolutionResponse = await localClient.PostAsJsonAsync(
            $"/api/sessions/{nominalSession!.AccessCode}/student-resolution", new ResolveStudentRequest("99000001"));
        Assert.True(resolutionResponse.StatusCode == HttpStatusCode.OK, await resolutionResponse.Content.ReadAsStringAsync());
        var resolution = await resolutionResponse.Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);

        var startResponse = await localClient.PostAsJsonAsync(
            $"/api/sessions/{nominalSession.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution!.ResolutionToken));
        Assert.True(startResponse.StatusCode == HttpStatusCode.Created, await startResponse.Content.ReadAsStringAsync());
        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.NotEmpty(started!.Blocks);

        var answerResponse = await localClient.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[] { new { blockId = block.Id, answer = "42" } }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await localClient.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.True(submitResponse.StatusCode == HttpStatusCode.OK, await submitResponse.Content.ReadAsStringAsync());
        var confirmation = await submitResponse.Content.ReadFromJsonAsync<SubmitAttemptResponse>();
        Assert.NotNull(confirmation);

        var attemptId = started.Attempt.Id;

        // 8. OFFLINE: read statistics for the same school code ("180055400") used by RunSessionAndSubmitAsync
        var statsResponse = await localClient.GetAsync("/api/stats/course?cue=180055400");
        Assert.True(statsResponse.StatusCode == HttpStatusCode.OK, await statsResponse.Content.ReadAsStringAsync());
        using (var statsDoc = JsonDocument.Parse(await statsResponse.Content.ReadAsStringAsync()))
        {
            var courses = statsDoc.RootElement.EnumerateArray().ToArray();
            Assert.NotEmpty(courses);
            var attemptCountElement = courses[0].GetProperty("attemptCount");
            var attemptCount = attemptCountElement.ValueKind == JsonValueKind.Number
                ? attemptCountElement.GetInt32()
                : int.Parse(attemptCountElement.GetString()!);
            Assert.Equal(1, attemptCount);
        }

        // 9. RECONNECT
        localFactory.Connectivity.Offline = false;

        // 10. Sync: push the outbox
        using (var pushScope = localFactory.Services.CreateScope())
        {
            var push = pushScope.ServiceProvider.GetRequiredService<LocalOutboxPushService>();
            var result = await push.PushAsync(50, CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Accepted >= 1, $"expected at least one accepted outbox item but got {result.Accepted}");
        }

        // 11. Verify on Central: the received attempt AND its independently recomputed grade
        CentralAttemptResult? centralResult;
        using (var centralScope = centralFactory.Services.CreateScope())
        {
            var db = centralScope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            var received = await db.ReceivedStudentAttempts.SingleOrDefaultAsync(x => x.RemoteLocalId == attemptId);
            Assert.NotNull(received);
            Assert.Equal("submitted", received.Status);

            centralResult = await db.CentralAttemptResults.SingleOrDefaultAsync(x => x.ReceivedStudentAttemptId == received.Id);
            Assert.NotNull(centralResult);
            Assert.Equal("graded", centralResult!.Status);
            Assert.NotNull(centralResult.Score);
            Assert.NotNull(centralResult.ScoreMax);
        }

        // 12. Compare against Local's own graded score for the same attempt — grading determinism (§7).
        //     attempt_results has no repository read method today; raw SQL is the correct approach,
        //     matching this repo's existing convention for ad hoc verification queries.
        using (var localScope = localFactory.Services.CreateScope())
        {
            var connectionFactory = localScope.ServiceProvider.GetRequiredService<ILocalSqliteConnectionFactory>();
            using var connection = connectionFactory.CreateOpenConnection();
            var localResult = await connection.QuerySingleAsync<(string Status, decimal? Score, decimal? ScoreMax)>(
                "SELECT status AS Status, score AS Score, score_max AS ScoreMax FROM attempt_results WHERE student_attempt_id = @Id",
                new { Id = attemptId });
            Assert.Equal("graded", localResult.Status);
            Assert.Equal(centralResult.Score, localResult.Score);
            Assert.Equal(centralResult.ScoreMax, localResult.ScoreMax);
        }

        // 13. Gated update feed — seeds a real registered node + release ring DIRECTLY via Central's
        //     DbContext, as an explicit named substitute: no admin endpoint exists yet to do this over
        //     HTTP (see _briefs/B7-PROGRESS.md, "Known gap, not closed in this PR"). This proves the
        //     SERVER side of the gated-update contract for a real registered node over real HTTP. It
        //     does NOT and CANNOT prove a client actually downloads, verifies, applies or restarts an
        //     update — that needs a real Windows machine and a vpk pack-produced installer, neither of
        //     which exist in this environment (same standard as B7-PROGRESS.md's PROVEN/NOT-PROVEN
        //     table). No assertion below claims otherwise.
        using (var centralScope = centralFactory.Services.CreateScope())
        {
            var db = centralScope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            db.RegisteredNodes.Add(new RegisteredNode(
                Id: "e2e-node-1", SchoolId: null, NodeCode: "e2e-node-1", DeviceName: null, Status: "active",
                LastSeenAt: null, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
                FingerprintHash: "e2e-fingerprint", FingerprintComponents: JsonDocument.Parse("{}"),
                Cue: "180055400", ActivationKeyId: null, EnrolledAt: DateTimeOffset.UtcNow,
                RevokedAt: null, AppVersion: "1.0.0"));
            db.Set<ReleaseRing>().Add(new ReleaseRing(
                Id: Guid.NewGuid(), Version: "9.9.9", Channel: "stable",
                Sha256: "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
                DownloadUrl: "https://updates.example/PlanCope-9.9.9-win-x64.zip",
                RolloutMode: "AllEnrolled", RolloutPercentage: null,
                CreatedAt: DateTimeOffset.UtcNow, CreatedBy: Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        var nodeAccessToken = new TokenService(Options.Create(new AuthOptions { SigningKey = CentralApiFactory.SigningKey }))
            .CreateNodeAccessToken("e2e-node-1", "180055400", TimeSpan.FromMinutes(5));

        using var updateCheckClient = centralFactory.CreateClient();
        updateCheckClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nodeAccessToken);
        var feedResponse = await updateCheckClient.GetAsync("/api/updates/releases.stable.json?id=PlanCope.Local.Host&localVersion=1.0.0");
        Assert.True(feedResponse.StatusCode == HttpStatusCode.OK, await feedResponse.Content.ReadAsStringAsync());
        using (var feedDoc = JsonDocument.Parse(await feedResponse.Content.ReadAsStringAsync()))
        {
            var asset = feedDoc.RootElement.GetProperty("Assets")[0];
            Assert.Equal("9.9.9", asset.GetProperty("Version").GetString());
            Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef", asset.GetProperty("SHA256").GetString());
        }

        // 14. Verify data survived: everything from the whole sequence above is still present.
        using (var finalScope = localFactory.Services.CreateScope())
        {
            var connectionFactory = finalScope.ServiceProvider.GetRequiredService<ILocalSqliteConnectionFactory>();
            using var connection = connectionFactory.CreateOpenConnection();
            var attemptStatus = await connection.QuerySingleAsync<string>(
                "SELECT status FROM student_attempts WHERE id = @Id", new { Id = attemptId });
            Assert.Equal("submitted", attemptStatus);
            var rosterStudentCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM local_roster_students WHERE snapshot_id = @SnapshotId", new { SnapshotId = "e2e-snapshot" });
            Assert.Equal(1, rosterStudentCount);
        }
        var finalStatsResponse = await localClient.GetAsync("/api/stats/course?cue=180055400");
        Assert.Equal(HttpStatusCode.OK, finalStatsResponse.StatusCode);

        Directory.Delete(bundleDir, recursive: true);
    }

    private static string CreateAccessToken()
    {
        var tokenService = new TokenService(Options.Create(new AuthOptions { SigningKey = CentralApiFactory.SigningKey }));
        return tokenService.CreateAccessToken(new UserProfileDto("e2e-user-id", "E2E User", "Admin", null, "province", []));
    }

    private static async Task<ExamCreated> CreateExamOnCentralAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/exams", new CreateExamRequest(
            "E2E-MAT-001", "Matematica E2E", null, "Primaria", "Matematica", "Matematica"));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var exam = await response.Content.ReadFromJsonAsync<ExamCreated>();
        Assert.NotNull(exam);
        Assert.False(string.IsNullOrWhiteSpace(exam!.Id));
        return exam;
    }

    private static async Task<VersionCreated> CreateExamVersionOnCentralAsync(HttpClient client, string examId)
    {
        var response = await client.PostAsJsonAsync($"/api/exams/{examId}/versions", new CreateExamVersionRequest(
            1, JsonSerializer.Deserialize<JsonElement>("{}"), "ProportionalPenalised"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<VersionCreated>();
        Assert.NotNull(version);
        Assert.False(string.IsNullOrWhiteSpace(version!.Id));
        return version;
    }

    private static async Task<BlockCreated> AddBlockOnCentralAsync(HttpClient client, string versionId)
    {
        var config = JsonSerializer.Deserialize<JsonElement>("""
            {"question":"Cuanto es 18 + 24?","options":[{"value":"42","label":"42"},{"value":"44","label":"44"}]}
            """);
        var response = await client.PutAsJsonAsync($"/api/exams/versions/{versionId}/blocks", new UpsertBlockRequest(
            0, BlockType.MultipleChoice, "Pregunta 1", null, config, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var block = await response.Content.ReadFromJsonAsync<BlockCreated>();
        Assert.NotNull(block);
        Assert.False(string.IsNullOrWhiteSpace(block!.Id));
        return block;
    }

    private static async Task PublishVersionOnCentralAsync(HttpClient client, string versionId)
    {
        var response = await client.PostAsJsonAsync($"/api/exams/versions/{versionId}/publish", new PublishExamVersionRequest(
            "Matematica", "6", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var publish = await response.Content.ReadFromJsonAsync<PublishExamVersionResponse>();
        Assert.NotNull(publish);
        Assert.False(string.IsNullOrWhiteSpace(publish!.Checksum));
    }

    private static async Task SeedSyncStateAsync(LocalApiFactory factory, string centralUrl, string accessToken)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISyncStateRepository>();
        var now = DateTimeOffset.UtcNow.ToString("O");
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "central_url", JsonSerializer.Serialize(centralUrl), now));
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "node_id", JsonSerializer.Serialize("e2e-node-1"), now));
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "central_access_token", JsonSerializer.Serialize(accessToken), now));
    }

    private static async Task<string> RunSessionAndSubmitAsync(HttpClient client, string examVersionId, string blockId)
    {
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            examVersionId, "180055400", "6A", null, "Operador", 30, null));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session!.AccessCode));

        var startResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.NotNull(started!.Attempt);
        Assert.NotEmpty(started.Blocks);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId, answer = "42" }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var confirmation = await submitResponse.Content.ReadFromJsonAsync<SubmitAttemptResponse>();
        Assert.NotNull(confirmation);
        Assert.False(string.IsNullOrWhiteSpace(confirmation!.ConfirmationCode));

        return started.Attempt.Id;
    }

    private sealed record ExamCreated(string Id);

    private sealed record VersionCreated(string Id);

    private sealed record BlockCreated(string Id);

    private sealed record StartAttemptResponse(StudentAttempt Attempt, IReadOnlyList<LocalExamBlock> Blocks);

    private sealed class CentralApiFactory : WebApplicationFactory<PlanCopeDbContext>
    {
        // Must be per-instance, not static: with two [Fact] methods in this class each
        // constructing their own CentralApiFactory, a static field would make every instance
        // in the same test-process share one InMemory database, breaking test isolation.
        private readonly string DbName = Guid.NewGuid().ToString("N");

        public const string SigningKey = "e2e-test-signing-key-with-at-least-64-characters-0123456789abcdef0123456789abcdef";

        public CentralApiFactory()
        {
            // builder.Configuration.GetSection("Auth") in Program.cs runs before
            // WebApplication.Build(), so ConfigureAppConfiguration below never reaches it in
            // time (same minimal-hosting-model ordering gotcha LocalApiFactory works around
            // with an environment variable for its connection string). An env var set before
            // the host starts is read by builder.Configuration like any other source.
            Environment.SetEnvironmentVariable("Auth__SigningKey", SigningKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:SigningKey"] = SigningKey
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<PlanCopeDbContext>>();
                services.AddDbContext<PlanCopeDbContext>(options =>
                    options.UseInMemoryDatabase(DbName)
                        .ReplaceService<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
                        // SyncController opens a real transaction, which the InMemory provider
                        // doesn't support - it only warns (and EF is configured to throw on
                        // warnings), it doesn't actually need one to behave correctly here.
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        // Local's sync services (LocalExamPullService/LocalOutboxPushService) build their own
        // HttpClient and set its BaseAddress from a "central_url" string read out of
        // sync_state at runtime - they are not aware this is a test. Rather than making
        // WebApplicationFactory bind a real Kestrel socket (fragile: WebApplicationFactory's
        // CreateHost never actually starts a real listener, it only wires TestServer's
        // in-memory transport, so a real HttpClient pointed at the reported address gets
        // "connection refused"), those two named clients get their PRIMARY HANDLER replaced
        // with TestServer's own in-memory handler in LocalApiFactory below. BaseAddress then
        // only needs to be a syntactically valid absolute URI - TestServer's handler dispatches
        // by request path, not by host/port - so this placeholder is fine to hand out as
        // "central_url".
        public string PlaceholderCentralUrl => Server.BaseAddress.ToString();

        public HttpMessageHandler CreateInProcessHandler() => Server.CreateHandler();
    }

    private sealed class LocalApiFactory : WebApplicationFactory<LocalDatabaseInitializer>
    {
        private readonly CentralApiFactory centralFactory;
        private readonly string bundlePath;
        private readonly string bundlePassphrase;
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-e2e-local-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public readonly SwitchableHandler Connectivity;

        public LocalApiFactory(CentralApiFactory centralFactory, string bundlePath = "", string bundlePassphrase = "")
        {
            this.centralFactory = centralFactory;
            this.bundlePath = bundlePath;
            this.bundlePassphrase = bundlePassphrase;
            Connectivity = new SwitchableHandler(centralFactory.CreateInProcessHandler());
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            previousSeedDemoExam = Environment.GetEnvironmentVariable("Local__SeedDemoExam");
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false",
                    ["Nominalization:DocumentHmacKey"] = "e2e-test-hmac-key-with-at-least-32-bytes",
                    ["RosterBundle:Path"] = bundlePath,
                    ["RosterBundle:Passphrase"] = bundlePassphrase
                });
            });
            builder.ConfigureServices(services =>
            {
                // Route Local's two Central-bound named HttpClients through Central's own
                // in-memory TestServer pipeline instead of a real socket - see the comment on
                // CentralApiFactory.CreateInProcessHandler for why. Both clients share the same
                // SwitchableHandler so flipping Connectivity.Offline gates them together.
                services.AddHttpClient(nameof(LocalExamPullService))
                    .ConfigurePrimaryHttpMessageHandler(() => Connectivity);
                services.AddHttpClient(nameof(LocalOutboxPushService))
                    .ConfigurePrimaryHttpMessageHandler(() => Connectivity);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", previousConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", previousSeedDemoExam);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class SwitchableHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker onlineInvoker;
        // Defaults to connected: the pre-existing test never touches this switch at all and
        // expects a working Central connection throughout. The new full-system test manages
        // both states explicitly at every step, so this default never matters to it.
        public volatile bool Offline;

        public SwitchableHandler(HttpMessageHandler onlineHandler) =>
            onlineInvoker = new HttpMessageInvoker(onlineHandler, disposeHandler: true);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("Simulated offline: no connectivity to Central.");
            }

            return onlineInvoker.SendAsync(request, cancellationToken);
        }
    }

}
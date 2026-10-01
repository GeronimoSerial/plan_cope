using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlanCope.Local.Api.Endpoints;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class EnrolmentEndpointsTests
{
    private const string Cue = "180055400";
    private const string ActivationKey = "test-activation-key";
    private const string RedeemedNodeId = "node-redeemed-42";

    [Fact]
    public async Task Redeem_with_valid_key_persists_central_credentials_and_activates_identity()
    {
        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // Real Central wraps the credential payload in ActivationRedeemResult; the old test
            // stub returned the bare response and masked the deserialization bug this covers.
            Content = JsonContent.Create(ActivationRedeemResult.Succeeded(new ActivationRedeemResponse
            {
                NodeId = RedeemedNodeId,
                AccessToken = "central-access-token",
                RefreshToken = "central-refresh-token",
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
            })),
        });

        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(RedeemedNodeId, body.RootElement.GetProperty("nodeId").GetString());
        }

        using var connection = factory.CreateConnection();
        Assert.Equal(RedeemedNodeId, ReadStateString(connection, "node_id"));
        Assert.Equal("central-access-token", ReadStateString(connection, "central_access_token"));
        Assert.Equal("central-refresh-token", ReadStateString(connection, "central_refresh_token"));
        Assert.Equal(accessTokenExpiresAt.ToString("O"), ReadStateString(connection, "central_access_token_expires_at"));
        Assert.Equal(refreshTokenExpiresAt.ToString("O"), ReadStateString(connection, "central_refresh_token_expires_at"));

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT node_id, credential_state, enrolled_at FROM node_identity;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(RedeemedNodeId, reader.GetString(0));
        Assert.Equal("active", reader.GetString(1));
        Assert.False(reader.IsDBNull(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Retry_download_uses_saved_credentials_without_redeeming_key_again()
    {
        var download = new RetryableInitialDownloadService();
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ActivationRedeemResult.Succeeded(new ActivationRedeemResponse
            {
                NodeId = RedeemedNodeId,
                AccessToken = "central-access-token",
                RefreshToken = "central-refresh-token",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            }))
        });
        using var factory = new EnrolmentApiFactory(handler, download);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var failedDownload = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));
        Assert.Equal(HttpStatusCode.BadGateway, failedDownload.StatusCode);
        using (var failureBody = JsonDocument.Parse(await failedDownload.Content.ReadAsStringAsync()))
            Assert.Equal("download failed", failureBody.RootElement.GetProperty("error").GetString());

        var pendingStatus = await client.GetFromJsonAsync<JsonElement>("/api/activation/status");
        Assert.True(pendingStatus.GetProperty("activationInProgress").GetBoolean());
        Assert.False(pendingStatus.GetProperty("isLocked").GetBoolean());
        Assert.True(pendingStatus.GetProperty("retryAvailable").GetBoolean());
        var blockedApiResponse = await client.GetAsync("/api/exams/");
        Assert.Equal(HttpStatusCode.Locked, blockedApiResponse.StatusCode);
        using (var blockedBody = JsonDocument.Parse(await blockedApiResponse.Content.ReadAsStringAsync()))
            Assert.Equal("activation_in_progress", blockedBody.RootElement.GetProperty("errorCode").GetString());

        var retry = await client.PostAsync("/api/enrolment/retry-download", null);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(2, download.CallCount);
    }

    [Fact]
    public async Task Redeeming_the_same_validated_key_again_retries_the_initial_download()
    {
        var download = new RetryableInitialDownloadService();
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ActivationRedeemResult.Succeeded(new ActivationRedeemResponse
            {
                NodeId = RedeemedNodeId,
                AccessToken = "central-access-token",
                RefreshToken = "central-refresh-token",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            }))
        });
        using var factory = new EnrolmentApiFactory(handler, download);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var firstAttempt = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));
        var secondAttempt = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.BadGateway, firstAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondAttempt.StatusCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, download.CallCount);
    }

    [Fact]
    public async Task Activation_status_disables_retry_for_a_revoked_locked_device()
    {
        using var factory = new EnrolmentApiFactory(new StubCentralHandler(() => throw new InvalidOperationException()));
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();
        factory.SetNodeCredentialState("revoked", "locked");
        factory.SetActivationFlag("activation_in_progress", true);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/activation/status");

        Assert.True(status.GetProperty("activationInProgress").GetBoolean());
        Assert.True(status.GetProperty("isLocked").GetBoolean());
        Assert.True(status.GetProperty("isRevoked").GetBoolean());
        Assert.False(status.GetProperty("retryAvailable").GetBoolean());
        var retry = await client.PostAsync("/api/enrolment/retry-download", null);
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
    }

    [Fact]
    public async Task Activation_status_exposes_revoked_state_before_locking()
    {
        using var factory = new EnrolmentApiFactory(new StubCentralHandler(() => throw new InvalidOperationException()));
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();
        factory.SetNodeCredentialState("revoked", null);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/activation/status");

        Assert.True(status.GetProperty("isRevoked").GetBoolean());
        Assert.False(status.GetProperty("isLocked").GetBoolean());
    }

    [Fact]
    public async Task Activation_status_disables_retry_after_a_crash_mid_expiry_wipe()
    {
        using var factory = new EnrolmentApiFactory(new StubCentralHandler(() => throw new InvalidOperationException()));
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();
        factory.SetNodeCredentialState("active", null);
        factory.SetActivationFlag("activation_in_progress", true);
        factory.SetActivationFlag("activation_expiry_pending", true);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/activation/status");

        Assert.True(status.GetProperty("activationInProgress").GetBoolean());
        Assert.True(status.GetProperty("expiryPending").GetBoolean());
        Assert.False(status.GetProperty("retryAvailable").GetBoolean());
        var retry = await client.PostAsync("/api/enrolment/retry-download", null);
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
    }

    [Fact]
    public async Task Redeem_accepts_bare_legacy_central_body()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // Older Central builds returned the credential fields at the root with no wrapper.
            Content = JsonContent.Create(new ActivationRedeemResponse
            {
                NodeId = RedeemedNodeId,
                AccessToken = "central-access-token",
                RefreshToken = "central-refresh-token",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            }),
        });

        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
        using var connection = factory.CreateConnection();
        Assert.Equal(RedeemedNodeId, ReadStateString(connection, "node_id"));
        Assert.Equal("central-access-token", ReadStateString(connection, "central_access_token"));
    }

    [Fact]
    public async Task Redeem_with_failed_wrapper_maps_reason_to_spanish_error()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"isSuccess":false,"reason":"KeyRevoked","response":null}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        });

        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains(
            "La clave de activación fue revocada.",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var connection = factory.CreateConnection();
        Assert.Null(ReadStateString(connection, "node_id"));
        Assert.Null(ReadStateString(connection, "central_access_token"));
    }

    [Fact]
    public async Task Redeem_revoked_node_maps_specific_spanish_message()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"isSuccess":false,"reason":"NodeRevoked","response":null}""", System.Text.Encoding.UTF8, "application/json")
        });
        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Este equipo fue dado de baja en Central", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Redeem_with_malformed_central_body_returns_bad_request_not_server_error()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json", System.Text.Encoding.UTF8, "application/json"),
        });

        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains(
            "Central devolvió una respuesta inválida.",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var connection = factory.CreateConnection();
        Assert.Null(ReadStateString(connection, "node_id"));
        Assert.Null(ReadStateString(connection, "central_access_token"));
    }

    [Fact]
    public async Task Redeem_with_rejected_key_returns_spanish_error_and_writes_no_credential()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains(
            "No se pudo validar la clave de activación. Verificá que sea correcta y no esté vencida o revocada.",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var connection = factory.CreateConnection();

        // Only the credential keys are asserted as absent: the endpoint upserts `central_url`
        // before the outbound call, and the background sync service writes its own bookkeeping
        // keys (`sync_offline`, `sync_next_attempt_at`), so those two rows are expected here.
        Assert.Null(ReadStateString(connection, "node_id"));
        Assert.Null(ReadStateString(connection, "central_access_token"));
        Assert.Null(ReadStateString(connection, "central_refresh_token"));
        Assert.Null(ReadStateString(connection, "central_access_token_expires_at"));
        Assert.Null(ReadStateString(connection, "central_refresh_token_expires_at"));

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT node_id, credential_state, enrolled_at FROM node_identity;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Equal("unenrolled", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Configured_central_url_overrides_a_stale_saved_url()
    {
        var handler = new StubCentralHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();
        using (var connection = factory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES ('central-url', 'central_url', '" +
                JsonSerializer.Serialize("http://stale-central.test/") + "', @Now);";
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        Assert.Equal("central.test", handler.LastRequestUri?.Host);
    }

    [Fact]
    public async Task Redeem_when_central_is_unreachable_surfaces_the_transport_failure()
    {
        var handler = new StubCentralHandler(() => throw new HttpRequestException("central.test is unreachable"));
        using var factory = new EnrolmentApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedNodeIdentity();

        var response = await client.PostAsJsonAsync("/api/enrolment/redeem", new EnrolmentRedeemRequest(ActivationKey));

        // OBSERVED BEHAVIOR (verified by running this test, not guessed): the endpoint has no
        // try/catch around `PostAsJsonAsync`, so the transport failure escapes it — but it does
        // not reach the test as an exception. The test host runs in the Development environment,
        // where WebApplication's implicit DeveloperExceptionPage middleware catches the unhandled
        // exception and writes a 500 with the stack trace as plain text, so the in-memory
        // TestServer client sees a normal response instead of a re-thrown exception. The
        // assertion below matches exactly that. Production code is deliberately left unchanged:
        // adding a try/catch to EnrolmentEndpoints would be a behavior change outside this work
        // order's scope.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("central.test is unreachable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    private static string? ReadStateString(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM sync_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var valueJson = command.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(valueJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(valueJson);
        return document.RootElement.GetString();
    }

    private sealed class StubCentralHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            try
            {
                return Task.FromResult(responder());
            }
            catch (Exception exception)
            {
                // A real transport failure surfaces as a faulted task, not as a synchronous throw.
                return Task.FromException<HttpResponseMessage>(exception);
            }
        }
    }

    private sealed class RetryableInitialDownloadService : IInitialActivationDownloadService
    {
        public int CallCount { get; private set; }

        public Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CallCount == 1
                ? new InitialActivationDownloadResult(false, "download failed")
                : new InitialActivationDownloadResult(true, null));
        }
    }

    private sealed class EnrolmentApiFactory : WebApplicationFactory<Program>
    {
        private const string CentralBaseUrl = "http://central.test/";

        private readonly HttpMessageHandler centralHandler;
        private readonly IInitialActivationDownloadService initialDownloadService;
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-enrolment-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public EnrolmentApiFactory(HttpMessageHandler centralHandler, IInitialActivationDownloadService? initialDownloadService = null)
        {
            this.centralHandler = centralHandler;
            this.initialDownloadService = initialDownloadService ?? new StubInitialDownloadService();
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            previousSeedDemoExam = Environment.GetEnvironmentVariable("Local__SeedDemoExam");
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", "false");
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Seeds the school row and the unenrolled node identity the endpoint requires before it
        /// ever calls Central; without it the endpoint short-circuits with a 400.
        /// </summary>
        public void SeedNodeIdentity()
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, """
                INSERT OR IGNORE INTO schools (cue, created_at) VALUES ($cue, $now);
                """,
                ("$cue", Cue),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));

            Execute(connection, transaction, """
                INSERT INTO node_identity (id, node_id, cue, fingerprint_hash, fingerprint_components_json, enrolled_at, last_sync_at, credential_state, revocation_detected_at, revocation_stage)
                VALUES ($id, NULL, $cue, $hash, $components, NULL, NULL, 'unenrolled', NULL, NULL);
                """,
                ("$id", "node-identity-test"),
                ("$cue", Cue),
                ("$hash", "sha256-test-fingerprint"),
                ("$components", """{"machineGuid":{"present":false,"value":null},"volumeSerial":{"present":false,"value":null},"cpuId":{"present":false,"value":null}}"""));

            transaction.Commit();
        }

        public void SetNodeCredentialState(string credentialState, string? revocationStage)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE node_identity SET credential_state = $credentialState, revocation_stage = $revocationStage;";
            command.Parameters.AddWithValue("$credentialState", credentialState);
            command.Parameters.AddWithValue("$revocationStage", (object?)revocationStage ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        public void SetActivationFlag(string key, bool value)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES ($id, $key, $value, $now) ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false",
                    ["Central:BaseUrl"] = CentralBaseUrl,
                });
            });

            // Replace only the primary transport handler of the named client, so the real
            // CentralCredentialHandler middleware keeps running in front of the stub.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInitialActivationDownloadService>();
                services.AddSingleton(initialDownloadService);
                services.AddHttpClient(nameof(EnrolmentEndpoints))
                    .ConfigurePrimaryHttpMessageHandler(() => centralHandler);
            });
        }

        private sealed class StubInitialDownloadService : IInitialActivationDownloadService
        {
            public Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new InitialActivationDownloadResult(true, null));
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

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, string Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}

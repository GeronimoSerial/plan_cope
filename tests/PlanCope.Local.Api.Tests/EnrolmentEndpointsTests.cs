using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Local.Api.Endpoints;
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
            Content = JsonContent.Create(new ActivationRedeemResponse
            {
                NodeId = RedeemedNodeId,
                AccessToken = "central-access-token",
                RefreshToken = "central-refresh-token",
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
            }),
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
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

    private sealed class EnrolmentApiFactory : WebApplicationFactory<Program>
    {
        private const string CentralBaseUrl = "http://central.test/";

        private readonly HttpMessageHandler centralHandler;
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-enrolment-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public EnrolmentApiFactory(HttpMessageHandler centralHandler)
        {
            this.centralHandler = centralHandler;
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
                services.AddHttpClient(nameof(EnrolmentEndpoints))
                    .ConfigurePrimaryHttpMessageHandler(() => centralHandler);
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

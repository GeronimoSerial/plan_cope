using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class PullExamsEndpointTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Pull_exams_when_not_enrolled_returns_409_with_spanish_message()
    {
        var handler = new StubCentralHandler();
        using var factory = new PullApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/sync/pull-exams", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("error", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("not_enrolled", body.RootElement.GetProperty("errorCode").GetString());
        Assert.Contains("vinculado", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Pull_exams_returns_updated_shape_for_one_new_exam_and_keeps_alias_route()
    {
        var handler = new StubCentralHandler
        {
            Response = BuildPage("ev-1", "EXA-1", "100"),
        };

        using var factory = new PullApiFactory(handler);
        using var client = factory.CreateClient();
        factory.SeedEnrolment();

        var response = await client.PostAsync("/api/sync/pull-exams", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("updated", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("newExams").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("updatedExams").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totalReceived").GetInt32());
        Assert.Equal("Se importó 1 examen nuevo.", body.RootElement.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("lastPullAt").GetString()));

        // The second call has an advanced cursor, so Central returns nothing new.
        handler.Response = BuildPage(null, null, null);
        var alias = await client.PostAsync("/api/sync/pull-exams-now", null);
        Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        using var aliasBody = JsonDocument.Parse(await alias.Content.ReadAsStringAsync());
        Assert.Equal("up_to_date", aliasBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("No hay exámenes nuevos.", aliasBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Exams_endpoint_lists_only_the_current_version_when_two_versions_exist()
    {
        using var factory = new PullApiFactory(new StubCentralHandler());
        using var client = factory.CreateClient();
        await client.GetAsync("/api/health");
        factory.SeedTwoVersionsOfOneExam();

        using var response = await client.GetAsync("/api/exams/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        var entries = body.RootElement.EnumerateArray().ToList();
        var current = Assert.Single(entries);
        Assert.Equal("ev-v2", current.GetProperty("id").GetString());
        Assert.Equal(2, current.GetProperty("versionNumber").GetInt32());
    }

    [Fact]
    public async Task Demo_exams_are_not_seeded_by_default_in_production()
    {
        using var factory = new DefaultSeedApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/exams/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    private static PullResponse BuildPage(string? examVersionId, string? examCode, string? ticks)
    {
        if (examVersionId is null)
        {
            return new PullResponse([], "0", false, new Dictionary<string, string>());
        }

        const string checksum = "checksum-ev-1";
        var package = new PublishedExamPackageDto(
            "pkg-ev-1",
            "exam-ev-1",
            examVersionId,
            examCode!,
            "Titulo",
            1,
            1,
            checksum,
            null,
            Array.Empty<BlockDto>(),
            Array.Empty<AnswerKeyDto>(),
            Array.Empty<PublishedAssetDto>(),
            [new PublicationTargetDto("grade", "6")],
            null);

        var item = new SyncItem(
            "publication_package",
            "pkg-ev-1",
            "upsert",
            JsonSerializer.SerializeToElement(package, WebOptions),
            DateTimeOffset.UtcNow.ToString("O"),
            checksum);

        return new PullResponse([item], ticks!, false, new Dictionary<string, string>());
    }

    private sealed class StubCentralHandler : HttpMessageHandler
    {
        public PullResponse Response { get; set; } = new([], "0", false, new Dictionary<string, string>());

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(Response, WebOptions), Encoding.UTF8, "application/json"),
            });
        }
    }

    private class DefaultSeedApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-pull-endpoint-default-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";
        protected string DatabasePath => databasePath;

        public DefaultSeedApiFactory()
        {
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Production (not Development) and no Local:SeedDemoExam override: the code default and
            // appsettings.json must both resolve to false.
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", previousConnectionString);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private sealed class PullApiFactory : DefaultSeedApiFactory
    {
        private readonly HttpMessageHandler centralHandler;

        public PullApiFactory(HttpMessageHandler centralHandler)
        {
            this.centralHandler = centralHandler;
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            return connection;
        }

        public void SeedEnrolment()
        {
            using var connection = CreateConnection();
            UpsertState(connection, "central_url", "http://central.test/");
            UpsertState(connection, "node_id", "node-1");
            UpsertState(connection, "central_access_token", "test-access-token");
        }

        public void SeedTwoVersionsOfOneExam()
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            InsertExamVersion(connection, transaction, "ev-v1", "rem-v1", "EXA-DUP", 1, """
                {"title":"Matematica","grade":"6"}
                """);
            InsertExamVersion(connection, transaction, "ev-v2", "rem-v2", "EXA-DUP", 2, """
                {"title":"Matematica","grade":"6"}
                """);
            transaction.Commit();
        }

        private static void InsertExamVersion(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string id,
            string remoteId,
            string examCode,
            int versionNumber,
            string metadataJson)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ($id, $remoteId, $code, $version, $checksum, $metadata, 1, $now);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$remoteId", remoteId);
            command.Parameters.AddWithValue("$code", examCode);
            command.Parameters.AddWithValue("$version", versionNumber);
            command.Parameters.AddWithValue("$checksum", $"checksum-{id}");
            command.Parameters.AddWithValue("$metadata", metadataJson);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = $"Data Source={DatabasePath};Pooling=False",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(nameof(LocalExamPullService))
                    .ConfigurePrimaryHttpMessageHandler(() => centralHandler);
            });
        }

        private static void UpsertState(SqliteConnection connection, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sync_state (id, key, value_json, updated_at)
                VALUES ($id, $key, $value, $now)
                ON CONFLICT (key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value, WebOptions));
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }
}

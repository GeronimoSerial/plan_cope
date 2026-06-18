using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalSessionFlowTests
{
    [Fact]
    public async Task Session_attempt_submit_flow_writes_pending_outbox_item()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");
        Assert.NotNull(progress);
        Assert.Equal(0, progress.StartedCount);

        var startResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);

        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.NotNull(started.Attempt);
        Assert.NotEmpty(started.Blocks);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.QuestionBlockId, answer = "42" }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var confirmation = await submitResponse.Content.ReadFromJsonAsync<SubmitAttemptResponse>();
        Assert.NotNull(confirmation);
        Assert.False(string.IsNullOrWhiteSpace(confirmation.ConfirmationCode));

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_type, aggregate_type, aggregate_id, payload_json, status
            FROM sync_outbox
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(SyncEventTypes.AttemptSubmitted, reader.GetString(0));
        Assert.Equal("student_attempt", reader.GetString(1));
        Assert.Equal(started.Attempt.Id, reader.GetString(2));
        Assert.Equal("pending", reader.GetString(4));

        using var payload = JsonDocument.Parse(reader.GetString(3));
        Assert.Equal("submitted", payload.RootElement.GetProperty("attempt").GetProperty("Status").GetString());
        Assert.Single(payload.RootElement.GetProperty("answers").EnumerateArray());
    }

    [Fact]
    public async Task Starting_attempt_for_missing_or_paused_session_returns_error()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var missingResponse = await client.PostAsync("/api/sessions/NOPE1/attempts", null);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        var session = await CreateSessionAsync(client);
        var pauseResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("paused"));
        Assert.Equal(HttpStatusCode.NoContent, pauseResponse.StatusCode);

        var pausedResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.BadRequest, pausedResponse.StatusCode);
    }

    [Fact]
    public async Task Submitted_attempt_cannot_be_edited_or_submitted_again()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await (await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null))
            .Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.QuestionBlockId, answer = "44" }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, answerResponse.StatusCode);

        var duplicateSubmitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateSubmitResponse.StatusCode);
    }

    [Fact]
    public async Task Invalid_session_request_returns_validation_problem()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest("", "", null, null, "", 0, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LocalDeliverySession> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId,
            "CUE-DEMO",
            "6A",
            null,
            "Operador",
            30,
            null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessCode));
        return session;
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record StartAttemptResponse(StudentAttempt Attempt, IReadOnlyList<LocalExamBlock> Blocks);

    private sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        public const string ExamVersionId = "test-matematica-6-v1";
        public string QuestionBlockId { get; } = "test-question-1";
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-local-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public LocalApiFactory()
        {
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

        public void SeedExam()
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ($id, $remoteId, $code, 1, 'test-checksum', '{"title":"Matematica 6","grade":"6","division":"A"}', 1, $now);
                """,
                ("$id", ExamVersionId),
                ("$remoteId", "remote-test-matematica-6-v1"),
                ("$code", "MAT-6-TEST"),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));

            Execute(connection, transaction, """
                INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                VALUES ($id, $examId, $remoteBlockId, 0, 'multiple_choice', '{"question":"Cuanto es 18 + 24?","options":[{"value":"42","label":"42"},{"value":"44","label":"44"}]}', NULL);
                """,
                ("$id", QuestionBlockId),
                ("$examId", ExamVersionId),
                ("$remoteBlockId", "remote-test-question-1"));

            transaction.Commit();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false"
                });
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

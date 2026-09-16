using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class AttemptGradingTests
{
    [Fact]
    public async Task Graded_attempt_persists_score_and_excludes_ungradable_blocks()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedGradableExam(scoringPolicy: "AllOrNothing");

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.MultipleChoiceBlockId, answer = (object)new[] { "b" } },
                new { blockId = factory.TrueFalseBlockId, answer = (object)true },
                new { blockId = factory.ShortAnswerBlockId, answer = (object)"hola" }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, score, score_max
            FROM attempt_results
            WHERE student_attempt_id = $attemptId;
            """;
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("graded", reader.GetString(0));
        Assert.Equal(3.0, reader.GetDouble(1));
        Assert.Equal(3.0, reader.GetDouble(2));
    }

    [Fact]
    public async Task Blank_answer_is_recorded_as_blank_not_incorrect()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedGradableExam(scoringPolicy: "AllOrNothing");

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.MultipleChoiceBlockId, answer = new[] { "b" } }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT blocks_json
            FROM attempt_results
            WHERE student_attempt_id = $attemptId;
            """;
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        using var json = JsonDocument.Parse(reader.GetString(0));
        var trueFalseBlock = json.RootElement
            .EnumerateArray()
            .Single(entry => entry.GetProperty("BlockId").GetString() == factory.TrueFalseBlockId);
        Assert.Equal("Blank", trueFalseBlock.GetProperty("Outcome").GetString());
    }

    [Fact]
    public async Task No_policy_exam_is_accepted_and_marked_ungradable()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedGradableExam(scoringPolicy: null, includeExtraBlocks: false);

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.MultipleChoiceBlockId, answer = new[] { "b" } }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, score, score_max
            FROM attempt_results
            WHERE student_attempt_id = $attemptId;
            """;
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("ungradable", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public async Task Answer_key_join_survives_local_and_remote_block_ids_differing()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExamWithDifferingBlockIds();

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.MultipleChoiceBlockId, answer = new[] { "b" } }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, score
            FROM attempt_results
            WHERE student_attempt_id = $attemptId;
            """;
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("graded", reader.GetString(0));
        Assert.Equal(1.0, reader.GetDouble(1));
    }

    [Fact]
    public async Task Outbox_payload_carries_exam_version_remote_id()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedGradableExam(scoringPolicy: "AllOrNothing");

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId = factory.MultipleChoiceBlockId, answer = new[] { "b" } }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM sync_outbox
            WHERE aggregate_id = $attemptId;
            """;
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        using var payload = JsonDocument.Parse(reader.GetString(0));
        Assert.Equal(factory.RemoteExamVersionId, payload.RootElement.GetProperty("examVersionRemoteId").GetString());
    }

    private static async Task<LocalDeliverySession> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId,
            "180055400",
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

    private static async Task<StartAttemptResponse> StartAttemptAsync(HttpClient client, string accessCode)
    {
        var response = await client.PostAsync($"/api/sessions/{accessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var started = await response.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.NotEmpty(started.Blocks);
        return started;
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record StartAttemptResponse(StudentAttempt Attempt, IReadOnlyList<LocalExamBlock> Blocks);

    private sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        public const string ExamVersionId = "gradable-exam-v1";
        public string RemoteExamVersionId { get; } = "remote-gradable-exam-v1";
        public string MultipleChoiceBlockId { get; } = "blk-local-1";
        public string TrueFalseBlockId { get; } = "blk-local-2";
        public string ShortAnswerBlockId { get; } = "blk-local-3";
        public string TextBlockId { get; } = "blk-local-4";
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-grading-{Guid.NewGuid():N}.db");
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

        public void SeedGradableExam(string? scoringPolicy, bool includeExtraBlocks = true)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at, scoring_policy)
                VALUES ($id, $remoteId, $code, 1, 'test-checksum', '{"title":"Gradable"}', 1, $now, $policy);
                """,
                ("$id", ExamVersionId),
                ("$remoteId", RemoteExamVersionId),
                ("$code", "GRD-1"),
                ("$now", DateTimeOffset.UtcNow.ToString("O")),
                ("$policy", scoringPolicy));

            Execute(connection, transaction, """
                INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                VALUES ($id, $examId, $remoteBlockId, 0, 'multiple_choice', $config, NULL);
                """,
                ("$id", MultipleChoiceBlockId),
                ("$examId", ExamVersionId),
                ("$remoteBlockId", "remote-blk-1"),
                ("$config", "{\"question\":\"Elegi b\",\"options\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"b\",\"label\":\"B\"}]}"));

            Execute(connection, transaction, """
                INSERT INTO local_answer_keys (id, local_exam_version_id, remote_block_id, correct_answer_json, score_value)
                VALUES ($id, $examId, $remoteBlockId, '["b"]', 1);
                """,
                ("$id", "ak-blk-1"),
                ("$examId", ExamVersionId),
                ("$remoteBlockId", "remote-blk-1"));

            if (includeExtraBlocks)
            {
                Execute(connection, transaction, """
                    INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                    VALUES ($id, $examId, $remoteBlockId, 1, 'true_false', '{"question":"Es verdadero?"}', NULL);
                    """,
                    ("$id", TrueFalseBlockId),
                    ("$examId", ExamVersionId),
                    ("$remoteBlockId", "remote-blk-2"));

                Execute(connection, transaction, """
                    INSERT INTO local_answer_keys (id, local_exam_version_id, remote_block_id, correct_answer_json, score_value)
                    VALUES ($id, $examId, $remoteBlockId, 'true', 1);
                    """,
                    ("$id", "ak-blk-2"),
                    ("$examId", ExamVersionId),
                    ("$remoteBlockId", "remote-blk-2"));

                Execute(connection, transaction, """
                    INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                    VALUES ($id, $examId, $remoteBlockId, 2, 'short_answer', '{"question":"Saluda"}', NULL);
                    """,
                    ("$id", ShortAnswerBlockId),
                    ("$examId", ExamVersionId),
                    ("$remoteBlockId", "remote-blk-3"));

                Execute(connection, transaction, """
                    INSERT INTO local_answer_keys (id, local_exam_version_id, remote_block_id, correct_answer_json, score_value)
                    VALUES ($id, $examId, $remoteBlockId, '{"accepted":["hola"]}', 1);
                    """,
                    ("$id", "ak-blk-3"),
                    ("$examId", ExamVersionId),
                    ("$remoteBlockId", "remote-blk-3"));

                Execute(connection, transaction, """
                    INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                    VALUES ($id, $examId, $remoteBlockId, 3, 'text', '{"text":"Lee"}', NULL);
                    """,
                    ("$id", TextBlockId),
                    ("$examId", ExamVersionId),
                    ("$remoteBlockId", "remote-blk-4"));
            }

            transaction.Commit();
        }

        public void SeedExamWithDifferingBlockIds()
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at, scoring_policy)
                VALUES ($id, $remoteId, $code, 1, 'test-checksum', '{"title":"Gradable"}', 1, $now, 'AllOrNothing');
                """,
                ("$id", ExamVersionId),
                ("$remoteId", RemoteExamVersionId),
                ("$code", "GRD-1"),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));

            Execute(connection, transaction, """
                INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                VALUES ($id, $examId, 'blk-remote-1', 0, 'multiple_choice', $config, NULL);
                """,
                ("$id", MultipleChoiceBlockId),
                ("$examId", ExamVersionId),
                ("$config", "{\"question\":\"Elegi b\",\"options\":[{\"value\":\"a\",\"label\":\"A\"},{\"value\":\"b\",\"label\":\"B\"}]}"));

            Execute(connection, transaction, """
                INSERT INTO local_answer_keys (id, local_exam_version_id, remote_block_id, correct_answer_json, score_value)
                VALUES ('ak-blk-1', $examId, 'blk-remote-1', '["b"]', 1);
                """,
                ("$examId", ExamVersionId));

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
                    ["Nominalization:DocumentHmacKey"] = LocalApiFactory.DocumentHmacKey
                });
            });
        }

        public const string DocumentHmacKey = "release-test-key-with-at-least-32-bytes";

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

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, string? Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, (object?)parameter.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}
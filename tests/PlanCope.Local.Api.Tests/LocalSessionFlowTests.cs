using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE aggregate_id = $attemptId;";
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
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

    [Fact]
    public async Task Nominal_session_links_to_a_ready_roster_snapshot_and_section()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.Equal("2026", session!.SchoolYear);
        Assert.Equal("snapshot-a", session.RosterSnapshotId);
        Assert.Equal("section-a", session.RosterSectionId);
    }

    [Fact]
    public async Task Nominal_session_rejects_a_section_from_another_snapshot()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-b", "section-b", "Ready");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 B", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-b"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no pertenece", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nominal_session_rejects_a_snapshot_for_another_cue_or_year()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-OTHER", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no corresponde", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nominal_resolution_requires_confirmation_and_stores_only_identity_snapshot()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");

        var session = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var createdSession = await session.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(createdSession);

        var resolutionResponse = await client.PostAsJsonAsync($"/api/sessions/{createdSession!.AccessCode}/student-resolution", new ResolveStudentRequest("12.345.678"));
        Assert.Equal(HttpStatusCode.OK, resolutionResponse.StatusCode);
        var resolution = await resolutionResponse.Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);
        Assert.Equal("**.***.5678", resolution!.Student.MaskedDocument);
        Assert.DoesNotContain("12345678", await resolutionResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var startResponse = await client.PostAsJsonAsync($"/api/sessions/{createdSession.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.Equal("GE:501", started!.Attempt.StudentCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT student_code, ge_person_id, student_first_name, student_last_name, document_last4, verification_source, verified_at FROM student_attempts;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("GE:501", reader.GetString(0));
        Assert.Equal(501, reader.GetInt32(1));
        Assert.Equal("5678", reader.GetString(4));
        Assert.Equal("ge_roster", reader.GetString(5));
    }

    [Fact]
    public async Task Nominal_resolution_cannot_be_reused_or_started_twice()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);

        var first = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution!.ResolutionToken));
        var second = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Nominal_resolution_rejects_unknown_student_and_expired_token()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);

        var unknown = await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("98.765.432"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);
        factory.ExpireResolution(resolution!.ResolutionToken);
        var expired = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
    }

    [Fact]
    public async Task Nominal_resolution_concurrent_confirmation_creates_one_attempt()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("CUE-DEMO", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "CUE-DEMO", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution!.ResolutionToken)),
            secondClient.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
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

        public void SeedRoster(string cue, string schoolYear, string snapshotId, string sectionId, string status)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, """
                INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
                VALUES ($id, $cue, $year, $fetched, $checksum, 1, 2, $status);
                """,
                ("$id", snapshotId), ("$cue", cue), ("$year", schoolYear),
                ("$fetched", DateTimeOffset.UtcNow.ToString("O")), ("$checksum", $"checksum-{snapshotId}"), ("$status", status));
            Execute(connection, transaction, """
                INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
                VALUES ($id, $snapshot, 100, '6º', 'A', 'Primario', 'Mañana');
                """,
                ("$id", sectionId), ("$snapshot", snapshotId));
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

        public void SeedRosterStudent(string snapshotId, string sectionId, string id, int gePersonId, string document, string firstName, string lastName)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO local_roster_students (id, snapshot_id, section_id, ge_person_id, document_hash, document_last4, first_name, last_name)
                VALUES ($id, $snapshot, $section, $person, $hash, $last4, $first, $last);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$snapshot", snapshotId);
            command.Parameters.AddWithValue("$section", sectionId);
            command.Parameters.AddWithValue("$person", gePersonId);
            command.Parameters.AddWithValue("$hash", ComputeDocumentHash(document));
            command.Parameters.AddWithValue("$last4", "5678");
            command.Parameters.AddWithValue("$first", firstName);
            command.Parameters.AddWithValue("$last", lastName);
            command.ExecuteNonQuery();
        }

        private static string ComputeDocumentHash(string document)
        {
            var normalized = new string(document.Where(char.IsDigit).ToArray());
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(DocumentHmacKey), Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        public void ExpireResolution(string token)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE student_resolutions SET expires_at = $expires WHERE token_hash = $hash;";
            command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant());
            command.ExecuteNonQuery();
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

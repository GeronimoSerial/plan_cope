using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsEmptyResultTests
{
    [Fact]
    public async Task Course_stats_return_empty_array_when_no_rollup_rows_match()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var response = await client.GetAsync("/api/stats/course?cue=180055400");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"STATUS: {response.StatusCode} BODY: {body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Exam_stats_return_empty_array_when_no_rollup_rows_match()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var response = await client.GetAsync("/api/stats/exam?cue=180055400");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"STATUS: {response.StatusCode} BODY: {body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Html_report_rejects_school_without_submitted_attempts()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var report = await client.GetAsync("/api/stats/report.html?cue=180055400");

        Assert.Equal(HttpStatusCode.BadRequest, report.StatusCode);
        Assert.Contains("exámenes entregados", await report.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Schools_with_attempts_returns_only_submitting_schools_and_submission_metadata()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        using var connection = factory.CreateConnection();
        LocalApiFactory.Execute(connection, "INSERT INTO schools (cue, name, created_at) VALUES ('180055401', 'Escuela sin entregas', datetime('now'));");
        LocalApiFactory.Execute(connection, "INSERT INTO schools (cue, name, created_at) VALUES ('180055400', 'Escuela entregas', datetime('now'));");
        LocalApiFactory.Execute(connection, "INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at) VALUES ('exam-a', 'remote-a', 'MAT', 1, 'chk', '{}', 1, datetime('now'));");
        LocalApiFactory.Execute(connection, "INSERT INTO delivery_sessions (id, exam_version_id, school_code, started_by, start_at, status, access_code, expected_student_count) VALUES ('session-a', 'exam-a', '180055400', 'test', datetime('now'), 'closed', 'AAA111', 1);");
        LocalApiFactory.Execute(connection, "INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code) VALUES ('attempt-a', 'session-a', 'student-a', 'submitted', '2026-09-30T10:00:00Z', '2026-09-30T10:12:00Z', 1, 'CONF1');");

        var response = await client.GetAsync("/api/schools?withAttempts=true");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var school = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("180055400", school.GetProperty("code").GetString());
        Assert.Equal(1, school.GetProperty("submittedAttemptCount").GetInt32());
        Assert.Equal("2026-09-30T10:12:00Z", school.GetProperty("lastSubmittedAt").GetString());
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-empty-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        public static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public LocalApiFactory()
        {
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
                    ["Nominalization:DocumentHmacKey"] = "release-test-key-with-at-least-32-bytes"
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
    }
}

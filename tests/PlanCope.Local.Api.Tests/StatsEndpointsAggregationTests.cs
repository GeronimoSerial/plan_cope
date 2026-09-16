using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Local.Api.Data.Repositories;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsEndpointsAggregationTests
{
    [Fact]
    public async Task Endpoint_aggregates_match_expected_totals_across_1200_attempts()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        const int attemptsPerSession = 600;
        var expectedAttemptCount = attemptsPerSession * 2;
        var expectedCorrectPerExam = attemptsPerSession / 2;
        var expectedIncorrectPerExam = attemptsPerSession / 2;
        var correctCount = 0;
        var incorrectCount = 0;
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var connection = factory.CreateConnection())
        using (var transaction = connection.BeginTransaction())
        {
            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO schools (cue, name, created_at) VALUES ('123456789', 'Escuela Test', @now);
                """,
                ("@now", now));

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ('exam-a', 'remote-exam-a', 'MAT-6', 1, 'chk', '{}', 1, @now);
                """,
                ("@now", now));

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ('exam-b', 'remote-exam-b', 'LEN-6', 1, 'chk', '{}', 1, @now);
                """,
                ("@now", now));

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
                VALUES ('snap-1', '123456789', '2026', @now, 'chk', 2, 2, 'current');
                """,
                ("@now", now));

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
                VALUES ('sec-6', 'snap-1', NULL, '6', NULL, NULL, NULL);
                """);

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
                VALUES ('sec-7', 'snap-1', NULL, '7', NULL, NULL, NULL);
                """);

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id)
                VALUES ('ds-a-6', 'exam-a', '123456789', NULL, NULL, 'test', @now, NULL, 'closed', NULL, 'AAA111', 1, '2026', 'snap-1', 'sec-6');
                """,
                ("@now", now));

            LocalApiFactory.Execute(connection, transaction, """
                INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id)
                VALUES ('ds-b-7', 'exam-b', '123456789', NULL, NULL, 'test', @now, NULL, 'closed', NULL, 'BBB222', 1, '2026', 'snap-1', 'sec-7');
                """,
                ("@now", now));

            for (var i = 0; i < expectedAttemptCount; i++)
            {
                var sessionId = i < attemptsPerSession ? "ds-a-6" : "ds-b-7";
                var isCorrect = i % 2 == 0;
                if (isCorrect)
                {
                    correctCount++;
                }
                else
                {
                    incorrectCount++;
                }

                LocalApiFactory.Execute(connection, transaction, """
                    INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code)
                    VALUES (@id, @sessionId, @studentCode, 'submitted', @now, @now, @sequence, @confirmationCode);
                    """,
                    ("@id", $"attempt-{i}"),
                    ("@sessionId", sessionId),
                    ("@studentCode", $"student-{i}"),
                    ("@now", now),
                    ("@sequence", i),
                    ("@confirmationCode", $"CONF{i:0000}"));

                var blocksJson = isCorrect
                    ? "[{\"BlockId\":\"blk-1\",\"Outcome\":\"Correct\",\"Score\":3.0,\"ScoreMax\":3.0}]"
                    : "[{\"BlockId\":\"blk-1\",\"Outcome\":\"Incorrect\",\"Score\":0.0,\"ScoreMax\":3.0}]";

                LocalApiFactory.Execute(connection, transaction, """
                    INSERT INTO attempt_results (id, student_attempt_id, grading_schema_version, scoring_policy, status, score, score_max, blocks_json, graded_at)
                    VALUES (@id, @attemptId, 1, 'AllOrNothing', 'graded', @score, 3.0, @blocksJson, @now);
                    """,
                    ("@id", $"result-{i}"),
                    ("@attemptId", $"attempt-{i}"),
                    ("@score", isCorrect ? 3.0 : 1.0),
                    ("@blocksJson", blocksJson),
                    ("@now", now));
            }

            transaction.Commit();
        }

        var expectedAveragePercent =
            (correctCount * 3.0 + incorrectCount * 1.0) / (expectedAttemptCount * 3.0) * 100.0;

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IStatsRollupRepository>();
            await repository.RebuildAllAsync();
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var schoolResponse = await client.GetAsync("/api/stats/school?cue=123456789&schoolYear=2026");
        var schoolBody = await schoolResponse.Content.ReadAsStringAsync();
        Assert.True(schoolResponse.StatusCode == HttpStatusCode.OK, $"SCHOOL BODY: {schoolBody}");
        Assert.Equal(HttpStatusCode.OK, schoolResponse.StatusCode);
        var school = JsonSerializer.Deserialize<SchoolStatsResponse>(
            await schoolResponse.Content.ReadAsStringAsync(), jsonOptions);
        Assert.NotNull(school);
        Assert.Equal(JsonValueKind.Number, school.AttemptCount.ValueKind);
        Assert.Equal(expectedAttemptCount, school.AttemptCount.GetInt32());
        Assert.Equal(JsonValueKind.Number, school.AverageScorePercent.ValueKind);
        Assert.InRange(school.AverageScorePercent.GetDouble(), expectedAveragePercent - 0.01, expectedAveragePercent + 0.01);

        var courseResponse = await client.GetAsync("/api/stats/course?cue=123456789&schoolYear=2026");
        Assert.Equal(HttpStatusCode.OK, courseResponse.StatusCode);
        var courses = JsonSerializer.Deserialize<CourseStatsResponse[]>(
            await courseResponse.Content.ReadAsStringAsync(), jsonOptions);
        Assert.NotNull(courses);
        Assert.Equal(2, courses.Length);
        foreach (var expectedCourse in new[] { "6", "7" })
        {
            var course = Assert.Single(courses, entry => entry.Course == expectedCourse);
            Assert.Equal(JsonValueKind.Number, course.AttemptCount.ValueKind);
            Assert.Equal(attemptsPerSession, course.AttemptCount.GetInt32());
            Assert.Equal(JsonValueKind.Number, course.AverageScorePercent.ValueKind);
            Assert.InRange(course.AverageScorePercent.GetDouble(), expectedAveragePercent - 0.01, expectedAveragePercent + 0.01);
        }

        var examResponse = await client.GetAsync("/api/stats/exam?cue=123456789&schoolYear=2026");
        Assert.Equal(HttpStatusCode.OK, examResponse.StatusCode);
        var exams = JsonSerializer.Deserialize<ExamStatsResponse[]>(
            await examResponse.Content.ReadAsStringAsync(), jsonOptions);
        Assert.NotNull(exams);
        Assert.Equal(2, exams.Length);
        foreach (var expectedExamVersionId in new[] { "exam-a", "exam-b" })
        {
            var exam = Assert.Single(exams, entry => entry.ExamVersionId == expectedExamVersionId);
            Assert.Equal(JsonValueKind.Number, exam.AttemptCount.ValueKind);
            Assert.Equal(attemptsPerSession, exam.AttemptCount.GetInt32());
            Assert.Equal(JsonValueKind.Number, exam.AverageScorePercent.ValueKind);
            Assert.InRange(exam.AverageScorePercent.GetDouble(), expectedAveragePercent - 0.01, expectedAveragePercent + 0.01);

            var block = Assert.Single(exam.Blocks);
            Assert.Equal("blk-1", block.BlockId);
            Assert.Equal(expectedCorrectPerExam, block.CorrectCount);
            Assert.Equal(expectedIncorrectPerExam, block.IncorrectCount);
            Assert.Equal(0, block.PartialCount);
            Assert.Equal(0, block.BlankCount);
            Assert.Equal(0, block.UngradableCount);
        }
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record SchoolStatsResponse(JsonElement AttemptCount, JsonElement AverageScorePercent);

    private sealed record CourseStatsResponse(string Course, JsonElement AttemptCount, JsonElement AverageScorePercent);

    private sealed record ExamStatsResponse(
        string ExamVersionId,
        JsonElement AttemptCount,
        JsonElement AverageScorePercent,
        IReadOnlyList<BlockStatsResponse> Blocks);

    private sealed record BlockStatsResponse(
        string BlockId,
        int CorrectCount,
        int PartialCount,
        int IncorrectCount,
        int BlankCount,
        int UngradableCount);

    private sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-aggregation-{Guid.NewGuid():N}.db");
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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false",
                    ["Nominalization:DocumentHmacKey"] = DocumentHmacKey
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

        public static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}

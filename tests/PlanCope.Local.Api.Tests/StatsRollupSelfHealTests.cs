using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsRollupSelfHealTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-selfheal-{Guid.NewGuid():N}.db");
    private readonly string connectionString;

    public StatsRollupSelfHealTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
    }

    [Fact]
    public async Task SelfHealIfInconsistentAsync_NoDrift_IsANoOp()
    {
        SeedContext("CUE_HEAL_1", "2025", "Matematica", "ev_heal_1");

        const string blocksJson =
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}]";

        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.0, scoreMax: 1.0, blocksJson);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");

        var updatedAtBefore = GetUpdatedAt("CUE_HEAL_1");

        await repository.SelfHealIfInconsistentAsync();

        Assert.Equal(1, Count("SELECT COUNT(*) FROM stats_rollups"));
        Assert.Equal(updatedAtBefore, GetUpdatedAt("CUE_HEAL_1"));
    }

    [Fact]
    public async Task SelfHealIfInconsistentAsync_RollupMissingAfterSimulatedCrash_RecoversExactCounts()
    {
        SeedContext("CUE_HEAL_2", "2025", "Lengua", "ev_heal_2");

        const string blocksJson =
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b2\",\"Outcome\":\"Incorrect\",\"Score\":0.0,\"ScoreMax\":1.0}]";

        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.0, scoreMax: 2.0, blocksJson);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);

        Assert.Equal(0, Count("SELECT COUNT(*) FROM stats_rollups"));

        await repository.SelfHealIfInconsistentAsync();

        Assert.Equal(1, Count("SELECT COUNT(*) FROM stats_rollups"));

        var rollup = GetRollup("CUE_HEAL_2");
        Assert.Equal(1, rollup.AttemptCount);
        Assert.Equal(1.0, rollup.ScoreSum);
        Assert.Equal(2.0, rollup.ScoreMaxSum);
    }

    private void SeedContext(string cue, string? schoolYear, string course, string examVersionId, string? rosterSectionId = "rs_1")
    {
        Execute($"INSERT INTO schools (cue, name) VALUES ('{cue}', 'Escuela de prueba');");
        Execute($"INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, schema_version, synced_at) " +
                $"VALUES ('{examVersionId}', 'remote-{examVersionId}', 'EXAM', 1, 'checksum', 1, '2025-01-01T00:00:00.0000000+00:00');");
        var snapshotYear = schoolYear ?? "2025";
        Execute($"INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status) " +
                $"VALUES ('snap_1', '{cue}', '{snapshotYear}', '2025-01-01T00:00:00.0000000+00:00', 'checksum', 1, 1, 'current');");

        if (rosterSectionId is not null)
        {
            Execute($"INSERT INTO local_roster_sections (id, snapshot_id, course) VALUES ('{rosterSectionId}', 'snap_1', '{course}');");
        }

        var yearValue = schoolYear is null ? "NULL" : $"'{schoolYear}'";
        var sectionValue = rosterSectionId is null ? "NULL" : $"'{rosterSectionId}'";
        Execute($"INSERT INTO delivery_sessions (id, exam_version_id, school_code, started_by, start_at, status, school_year, roster_section_id) " +
                $"VALUES ('ds_1', '{examVersionId}', '{cue}', 'user_1', '2025-01-01T00:00:00.0000000+00:00', 'active', {yearValue}, {sectionValue});");
    }

    private void SeedAttempt(string attemptId, string deliverySessionId, string studentCode, int localSequence, double score, double scoreMax, string blocksJson, string status = "graded")
    {
        Execute($"INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, local_sequence) " +
                $"VALUES ('{attemptId}', '{deliverySessionId}', '{studentCode}', 'submitted', '2025-01-01T00:00:00.0000000+00:00', {localSequence});");
        Execute($"INSERT INTO attempt_results (id, student_attempt_id, grading_schema_version, status, score, score_max, blocks_json, graded_at) " +
                $"VALUES ('ar_{attemptId}', '{attemptId}', 1, '{status}', {score}, {scoreMax}, '{blocksJson}', '2025-01-01T00:00:00.0000000+00:00');");
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private int Count(string sql) => Convert.ToInt32(Scalar(sql));

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private (string Id, int AttemptCount, double ScoreSum, double ScoreMaxSum) GetRollup(string cue)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, attempt_count, score_sum, score_max_sum FROM stats_rollups WHERE cue = @cue";
        command.Parameters.AddWithValue("@cue", cue);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"No stats_rollups row for cue '{cue}'.");
        }
        return (reader.GetString(0), reader.GetInt32(1), reader.GetDouble(2), reader.GetDouble(3));
    }

    private string GetUpdatedAt(string cue)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM stats_rollups WHERE cue = @cue";
        command.Parameters.AddWithValue("@cue", cue);
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private sealed class TestConnectionFactory : ILocalSqliteConnectionFactory
    {
        private readonly string _connectionString;

        public TestConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqliteConnection CreateOpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsRollupIncrementalTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-incremental-{Guid.NewGuid():N}.db");
    private readonly string connectionString;

    public StatsRollupIncrementalTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
    }

    [Fact]
    public async Task UpsertForAttemptAsync_SingleGradedAttemptWithMixedOutcomes_WritesRollupAndPerBlockCounters()
    {
        SeedContext("CUE_1", "2025", "Matematica", "ev_1");

        const string blocksJson =
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b2\",\"Outcome\":\"Partial\",\"Score\":0.5,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b3\",\"Outcome\":\"Incorrect\",\"Score\":0.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b4\",\"Outcome\":\"Blank\",\"Score\":0.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b5\",\"Outcome\":\"Ungradable\",\"Score\":0.0,\"ScoreMax\":1.0}]";

        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.5, scoreMax: 5.0, blocksJson);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");

        Assert.Equal(1, Count("SELECT COUNT(*) FROM stats_rollups"));
        Assert.Equal(5, Count("SELECT COUNT(*) FROM stats_rollup_blocks"));

        var rollup = GetRollup("CUE_1");
        Assert.Equal(1, rollup.AttemptCount);
        Assert.Equal(1.5, rollup.ScoreSum);
        Assert.Equal(5.0, rollup.ScoreMaxSum);

        AssertBlock(rollup.Id, "b1", correct: 1, partial: 0, incorrect: 0, blank: 0, ungradable: 0, scoreSum: 1.0, scoreMaxSum: 1.0);
        AssertBlock(rollup.Id, "b2", correct: 0, partial: 1, incorrect: 0, blank: 0, ungradable: 0, scoreSum: 0.5, scoreMaxSum: 1.0);
        AssertBlock(rollup.Id, "b3", correct: 0, partial: 0, incorrect: 1, blank: 0, ungradable: 0, scoreSum: 0.0, scoreMaxSum: 1.0);
        AssertBlock(rollup.Id, "b4", correct: 0, partial: 0, incorrect: 0, blank: 1, ungradable: 0, scoreSum: 0.0, scoreMaxSum: 1.0);
        AssertBlock(rollup.Id, "b5", correct: 0, partial: 0, incorrect: 0, blank: 0, ungradable: 1, scoreSum: 0.0, scoreMaxSum: 1.0);
    }

    [Fact]
    public async Task UpsertForAttemptAsync_TwoAttemptsInSameTuple_AccumulatesCountsAndSums()
    {
        SeedContext("CUE_2", "2025", "Lengua", "ev_2");

        const string attemptABlocks =
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b2\",\"Outcome\":\"Blank\",\"Score\":0.0,\"ScoreMax\":1.0}]";

        const string attemptBBlocks =
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Incorrect\",\"Score\":0.0,\"ScoreMax\":1.0}," +
            "{\"BlockId\":\"b2\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}]";

        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.0, scoreMax: 2.0, attemptABlocks);
        SeedAttempt("att_2", "ds_1", "S_002", localSequence: 2, score: 1.0, scoreMax: 2.0, attemptBBlocks);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");
        await repository.UpsertForAttemptAsync("att_2");

        Assert.Equal(1, Count("SELECT COUNT(*) FROM stats_rollups"));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM stats_rollup_blocks"));

        var rollup = GetRollup("CUE_2");
        Assert.Equal(2, rollup.AttemptCount);
        Assert.Equal(2.0, rollup.ScoreSum);
        Assert.Equal(4.0, rollup.ScoreMaxSum);

        AssertBlock(rollup.Id, "b1", correct: 1, partial: 0, incorrect: 1, blank: 0, ungradable: 0, scoreSum: 1.0, scoreMaxSum: 2.0);
        AssertBlock(rollup.Id, "b2", correct: 1, partial: 0, incorrect: 0, blank: 1, ungradable: 0, scoreSum: 1.0, scoreMaxSum: 2.0);
    }

    [Fact]
    public async Task UpsertForAttemptAsync_AttemptWithoutRosterSection_DoesNotWriteRollup()
    {
        SeedContext("CUE_3", "2025", "Matematica", "ev_3", rosterSectionId: null);

        const string blocksJson = "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}]";
        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.0, scoreMax: 1.0, blocksJson);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");

        Assert.Equal(0, Count("SELECT COUNT(*) FROM stats_rollups"));
    }

    [Fact]
    public async Task UpsertForAttemptAsync_AttemptWithoutSchoolYear_DoesNotWriteRollup()
    {
        SeedContext("CUE_4", null, "Matematica", "ev_4");

        const string blocksJson = "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1.0,\"ScoreMax\":1.0}]";
        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 1.0, scoreMax: 1.0, blocksJson);

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");

        Assert.Equal(0, Count("SELECT COUNT(*) FROM stats_rollups"));
    }

    [Fact]
    public async Task UpsertForAttemptAsync_UngradableAttempt_DoesNotWriteRollup()
    {
        SeedContext("CUE_5", "2025", "Matematica", "ev_5");

        const string blocksJson = "[]";
        SeedAttempt("att_1", "ds_1", "S_001", localSequence: 1, score: 0.0, scoreMax: 0.0, blocksJson, status: "ungradable");

        var repository = new StatsRollupRepository(new TestConnectionFactory(connectionString), NullLogger<StatsRollupRepository>.Instance);
        await repository.UpsertForAttemptAsync("att_1");

        Assert.Equal(0, Count("SELECT COUNT(*) FROM stats_rollups"));
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

    private void AssertBlock(string rollupId, string blockId, int correct, int partial, int incorrect, int blank, int ungradable, double scoreSum, double scoreMaxSum)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT correct_count, partial_count, incorrect_count, blank_count, ungradable_count, score_sum, score_max_sum " +
                              "FROM stats_rollup_blocks WHERE rollup_id = @rollupId AND block_id = @blockId";
        command.Parameters.AddWithValue("@rollupId", rollupId);
        command.Parameters.AddWithValue("@blockId", blockId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"No stats_rollup_blocks row for rollup '{rollupId}' / block '{blockId}'.");
        }
        Assert.Equal(correct, reader.GetInt32(0));
        Assert.Equal(partial, reader.GetInt32(1));
        Assert.Equal(incorrect, reader.GetInt32(2));
        Assert.Equal(blank, reader.GetInt32(3));
        Assert.Equal(ungradable, reader.GetInt32(4));
        Assert.Equal(scoreSum, reader.GetDouble(5));
        Assert.Equal(scoreMaxSum, reader.GetDouble(6));
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
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsRollupReconciliationTests : IDisposable
{
    private const string SchoolYear = "2025";

    private static readonly string[] BlockIds = { "b1", "b2", "b3", "b4" };
    private static readonly string[] Outcomes = { "Correct", "Partial", "Incorrect", "Blank", "Ungradable" };

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-reconcile-{Guid.NewGuid():N}.db");
    private readonly string connectionString;

    public StatsRollupReconciliationTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
    }

    [Fact]
    public async Task RebuildAllAsync_MatchesIncrementalUpsertsExactly()
    {
        var random = new Random(12345);
        var repository = new StatsRollupRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)), NullLogger<StatsRollupRepository>.Instance);

        var cue = "CUE-12345";
        var examVersions = new[] { "exam-a", "exam-b" };
        var sections = new[] { ("sec-a", "MATEMATICA"), ("sec-b", "LENGUA"), ("sec-c", (string?)null) };

        SeedSchool(cue);
        foreach (var examVersion in examVersions)
        {
            SeedExamVersion(examVersion);
        }
        SeedRosterSnapshot("snap-1", cue, SchoolYear);
        foreach (var (sectionId, course) in sections)
        {
            SeedRosterSection(sectionId, "snap-1", course);
        }

        const int attemptCount = 60;
        var attemptIds = new List<string>(attemptCount);
        for (var i = 0; i < attemptCount; i++)
        {
            var (sectionId, _) = sections[random.Next(sections.Length)];
            var examVersion = examVersions[random.Next(examVersions.Length)];
            attemptIds.Add(SeedGradedAttempt($"sa-{i}", $"ds-{i}", cue, SchoolYear, sectionId, examVersion, random));
        }

        foreach (var attemptId in attemptIds.OrderBy(_ => random.Next()))
        {
            await repository.UpsertForAttemptAsync(attemptId);
        }

        var rollupsBeforeRebuild = SnapshotRollups();
        var blocksBeforeRebuild = SnapshotBlocks();

        await repository.RebuildAllAsync();

        // id and updated_at are excluded: a rebuild regenerates them by design, everything else must match exactly.
        Assert.Equal(rollupsBeforeRebuild, SnapshotRollups());
        Assert.Equal(blocksBeforeRebuild, SnapshotBlocks());
    }

    [Fact]
    public async Task RebuildTupleAsync_PicksLatestGradingOnly_AfterRegrade()
    {
        var repository = new StatsRollupRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)), NullLogger<StatsRollupRepository>.Instance);

        var cue = "CUE-REG";
        const string course = "MATEMATICA";
        const string examVersion = "exam-reg";
        const string attemptId = "sa-reg";

        SeedSchool(cue);
        SeedExamVersion(examVersion);
        SeedRosterSnapshot("snap-reg", cue, SchoolYear);
        SeedRosterSection("sec-reg", "snap-reg", course);
        SeedDeliverySession("ds-reg", examVersion, cue, SchoolYear, "sec-reg");
        SeedAttempt(attemptId, "ds-reg", "stu-reg");

        // v1 grading: b1 Incorrect, b2 Correct.
        SeedAttemptResult("ar-reg-v1", attemptId, 1, 1.0, 2.0,
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Incorrect\",\"Score\":0,\"ScoreMax\":1},{\"BlockId\":\"b2\",\"Outcome\":\"Correct\",\"Score\":1,\"ScoreMax\":1}]");

        await repository.UpsertForAttemptAsync(attemptId);

        // Re-grade with a higher schema version that changes the outcome; the incremental path is not re-run for a re-grade.
        SeedAttemptResult("ar-reg-v2", attemptId, 2, 2.0, 2.0,
            "[{\"BlockId\":\"b1\",\"Outcome\":\"Correct\",\"Score\":1,\"ScoreMax\":1},{\"BlockId\":\"b2\",\"Outcome\":\"Correct\",\"Score\":1,\"ScoreMax\":1}]");

        await repository.RebuildTupleAsync(cue, SchoolYear, course, examVersion);

        var rollup = Assert.Single(SnapshotRollups());
        Assert.Equal(1L, rollup.AttemptCount);
        Assert.Equal(2.0, rollup.ScoreSum);
        Assert.Equal(2.0, rollup.ScoreMaxSum);

        var blocks = SnapshotBlocks().OrderBy(block => block.BlockId).ToList();
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block =>
        {
            Assert.Equal(1L, block.Correct);
            Assert.Equal(0L, block.Incorrect);
            Assert.Equal(0L, block.Blank);
            Assert.Equal(1.0, block.ScoreSum);
            Assert.Equal(1.0, block.ScoreMaxSum);
        });
    }

    [Fact]
    public async Task RebuildTupleAsync_EmptyTuple_LeavesNoRollupRow()
    {
        var repository = new StatsRollupRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)), NullLogger<StatsRollupRepository>.Instance);

        var cue = "CUE-EMPTY";
        const string course = "LENGUA";
        const string examVersion = "exam-empty";

        SeedSchool(cue);
        SeedExamVersion(examVersion);
        SeedRosterSnapshot("snap-empty", cue, SchoolYear);
        SeedRosterSection("sec-empty", "snap-empty", course);

        await repository.RebuildTupleAsync(cue, SchoolYear, course, examVersion);

        Assert.Empty(SnapshotRollups());
        Assert.Empty(SnapshotBlocks());

        // A stale rollup for this tuple must be removed by the rebuild, not left behind as a zeroed row.
        Execute(@"
            INSERT INTO stats_rollups (id, cue, school_year, course, exam_version_id, attempt_count, score_sum, score_max_sum, updated_at)
            VALUES ('stale-rollup', @Cue, @SchoolYear, @Course, @ExamVersion, 5, 12.0, 20.0, '2025-01-01T00:00:00Z');",
            new { Cue = cue, SchoolYear = SchoolYear, Course = course, ExamVersion = examVersion });
        Execute(@"
            INSERT INTO stats_rollup_blocks (id, rollup_id, block_id, correct_count, partial_count, incorrect_count, blank_count, ungradable_count, score_sum, score_max_sum)
            VALUES ('stale-block', 'stale-rollup', 'b1', 5, 0, 0, 0, 0, 5.0, 5.0);");

        await repository.RebuildTupleAsync(cue, SchoolYear, course, examVersion);

        Assert.Empty(SnapshotRollups());
        Assert.Empty(SnapshotBlocks());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private string SeedGradedAttempt(string attemptId, string deliverySessionId, string cue, string schoolYear, string sectionId, string examVersion, Random random)
    {
        SeedDeliverySession(deliverySessionId, examVersion, cue, schoolYear, sectionId);
        SeedAttempt(attemptId, deliverySessionId, $"stu-{attemptId}");

        var blockCount = random.Next(1, BlockIds.Length + 1);
        var blocksJson = new List<string>(blockCount);
        var score = 0.0;
        for (var i = 0; i < blockCount; i++)
        {
            var outcome = Outcomes[random.Next(Outcomes.Length)];
            var blockScore = outcome is "Correct" or "Partial" ? 1.0 : 0.0;
            score += blockScore;
            blocksJson.Add($"{{\"BlockId\":\"{BlockIds[i]}\",\"Outcome\":\"{outcome}\",\"Score\":{blockScore},\"ScoreMax\":1}}");
        }

        SeedAttemptResult($"ar-{attemptId}", attemptId, 1, score, blockCount, $"[{string.Join(",", blocksJson)}]");

        return attemptId;
    }

    private void SeedDeliverySession(string id, string examVersion, string cue, string schoolYear, string sectionId)
    {
        Execute(@"
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, school_year, roster_section_id, started_by, start_at, status, expected_student_count)
            VALUES (@Id, @ExamVersion, @Cue, @SchoolYear, @SectionId, 'user-1', '2025-01-01T00:00:00Z', 'completed', 1);",
            new { Id = id, ExamVersion = examVersion, Cue = cue, SchoolYear = schoolYear, SectionId = sectionId });
    }

    private void SeedAttempt(string id, string deliverySessionId, string studentCode)
    {
        Execute(@"
            INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, local_sequence)
            VALUES (@Id, @DeliverySessionId, @StudentCode, 'submitted', '2025-01-01T00:00:00Z', 1);",
            new { Id = id, DeliverySessionId = deliverySessionId, StudentCode = studentCode });
    }

    private void SeedAttemptResult(string id, string attemptId, int schemaVersion, double score, double scoreMax, string blocksJson)
    {
        Execute(@"
            INSERT INTO attempt_results (id, student_attempt_id, grading_schema_version, status, score, score_max, blocks_json, graded_at)
            VALUES (@Id, @AttemptId, @SchemaVersion, 'graded', @Score, @ScoreMax, @BlocksJson, '2025-01-01T00:00:00Z');",
            new { Id = id, AttemptId = attemptId, SchemaVersion = schemaVersion, Score = score, ScoreMax = scoreMax, BlocksJson = blocksJson });
    }

    private void SeedSchool(string cue)
    {
        Execute("INSERT INTO schools (cue, name) VALUES (@Cue, 'School');", new { Cue = cue });
    }

    private void SeedExamVersion(string id)
    {
        Execute(@"
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, schema_version, synced_at)
            VALUES (@Id, @RemoteId, 'EXAM-1', 1, 'checksum-1', 1, '2025-01-01T00:00:00Z');",
            new { Id = id, RemoteId = $"remote-{id}" });
    }

    private void SeedRosterSnapshot(string id, string cue, string schoolYear)
    {
        Execute(@"
            INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
            VALUES (@Id, @Cue, @SchoolYear, '2025-01-01T00:00:00Z', 'checksum-1', 1, 1, 'complete');",
            new { Id = id, Cue = cue, SchoolYear = schoolYear });
    }

    private void SeedRosterSection(string id, string snapshotId, string? course)
    {
        Execute(@"
            INSERT INTO local_roster_sections (id, snapshot_id, course)
            VALUES (@Id, @SnapshotId, @Course);",
            new { Id = id, SnapshotId = snapshotId, Course = course });
    }

    private void Execute(string sql, object? parameters = null)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Execute(sql, parameters);
    }

    private List<RollupSnapshotRow> SnapshotRollups()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection.Query<RollupSnapshotRow>(@"
            SELECT cue AS Cue, school_year AS SchoolYear, course AS Course, exam_version_id AS ExamVersionId,
                   attempt_count AS AttemptCount, score_sum AS ScoreSum, score_max_sum AS ScoreMaxSum
            FROM stats_rollups
            ORDER BY cue, school_year, course, exam_version_id").ToList();
    }

    private List<BlockSnapshotRow> SnapshotBlocks()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection.Query<BlockSnapshotRow>(@"
            SELECT r.cue AS Cue, r.school_year AS SchoolYear, r.course AS Course, r.exam_version_id AS ExamVersionId,
                   b.block_id AS BlockId,
                   b.correct_count AS Correct, b.partial_count AS Partial, b.incorrect_count AS Incorrect,
                   b.blank_count AS Blank, b.ungradable_count AS Ungradable,
                   b.score_sum AS ScoreSum, b.score_max_sum AS ScoreMaxSum
            FROM stats_rollup_blocks b
            JOIN stats_rollups r ON r.id = b.rollup_id
            ORDER BY r.cue, r.school_year, r.course, r.exam_version_id, b.block_id").ToList();
    }

    private sealed record RollupSnapshotRow(string Cue, string SchoolYear, string Course, string ExamVersionId, long AttemptCount, double ScoreSum, double ScoreMaxSum);

    private sealed record BlockSnapshotRow(string Cue, string SchoolYear, string Course, string ExamVersionId, string BlockId, long Correct, long Partial, long Incorrect, long Blank, long Ungradable, double ScoreSum, double ScoreMaxSum);
}
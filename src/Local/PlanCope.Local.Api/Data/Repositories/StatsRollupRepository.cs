using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PlanCope.Local.Api.Data;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class StatsRollupRepository : IStatsRollupRepository
{
    private const string UnassignedCourse = "sin_asignar";

    private readonly ILocalSqliteConnectionFactory _connectionFactory;
    private readonly ILogger<StatsRollupRepository> _logger;

    public StatsRollupRepository(ILocalSqliteConnectionFactory connectionFactory, ILogger<StatsRollupRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task UpsertForAttemptAsync(string studentAttemptId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        const string tupleSql = @"
            SELECT ds.school_code        AS SchoolCode,
                   ds.school_year        AS SchoolYear,
                   ds.roster_section_id  AS RosterSectionId,
                   ds.exam_version_id    AS ExamVersionId,
                   lrs.course            AS Course,
                   ar.status             AS Status,
                   ar.score              AS Score,
                   ar.score_max          AS ScoreMax,
                   ar.blocks_json        AS BlocksJson
            FROM student_attempts sa
            JOIN delivery_sessions ds ON ds.id = sa.delivery_session_id
            LEFT JOIN local_roster_sections lrs ON lrs.id = ds.roster_section_id
            LEFT JOIN attempt_results ar
                   ON ar.student_attempt_id = sa.id
                  AND ar.grading_schema_version = (SELECT MAX(grading_schema_version)
                                                     FROM attempt_results
                                                    WHERE student_attempt_id = sa.id)
            WHERE sa.id = @StudentAttemptId";

        var tuple = await connection.QuerySingleOrDefaultAsync<AttemptTupleRow>(
            new CommandDefinition(tupleSql, new { StudentAttemptId = studentAttemptId }, cancellationToken: cancellationToken));

        if (tuple is null
            || tuple.SchoolYear is null
            || tuple.RosterSectionId is null
            || tuple.Status != "graded"
            || tuple.BlocksJson is null)
        {
            return;
        }

        var course = string.IsNullOrEmpty(tuple.Course) ? UnassignedCourse : tuple.Course;
        var updatedAt = DateTimeOffset.UtcNow.ToString("O");

        const string upsertRollupSql = @"
            INSERT INTO stats_rollups (id, cue, school_year, course, exam_version_id, attempt_count, score_sum, score_max_sum, updated_at)
            VALUES (@Id, @Cue, @SchoolYear, @Course, @ExamVersionId, 1, @Score, @ScoreMax, @UpdatedAt)
            ON CONFLICT (cue, school_year, course, exam_version_id) DO UPDATE SET
                attempt_count = attempt_count + 1,
                score_sum = score_sum + excluded.score_sum,
                score_max_sum = score_max_sum + excluded.score_max_sum,
                updated_at = excluded.updated_at";

        await connection.ExecuteAsync(new CommandDefinition(
            upsertRollupSql,
            new
            {
                Id = Guid.NewGuid().ToString(),
                Cue = tuple.SchoolCode,
                SchoolYear = tuple.SchoolYear,
                Course = course,
                ExamVersionId = tuple.ExamVersionId,
                Score = tuple.Score,
                ScoreMax = tuple.ScoreMax,
                UpdatedAt = updatedAt,
            },
            cancellationToken: cancellationToken));

        const string selectRollupIdSql = @"
            SELECT id
            FROM stats_rollups
            WHERE cue = @Cue AND school_year = @SchoolYear AND course = @Course AND exam_version_id = @ExamVersionId";

        var rollupId = await connection.QuerySingleAsync<string>(new CommandDefinition(
            selectRollupIdSql,
            new
            {
                Cue = tuple.SchoolCode,
                SchoolYear = tuple.SchoolYear,
                Course = course,
                ExamVersionId = tuple.ExamVersionId,
            },
            cancellationToken: cancellationToken));

        var blocks = JsonSerializer.Deserialize<BlockOutcomeRow[]>(tuple.BlocksJson);
        if (blocks is null || blocks.Length == 0)
        {
            return;
        }

        const string upsertBlockSql = @"
            INSERT INTO stats_rollup_blocks (id, rollup_id, block_id, correct_count, partial_count, incorrect_count, blank_count, ungradable_count, score_sum, score_max_sum)
            VALUES (@Id, @RollupId, @BlockId, @Correct, @Partial, @Incorrect, @Blank, @Ungradable, @Score, @ScoreMax)
            ON CONFLICT (rollup_id, block_id) DO UPDATE SET
                correct_count = correct_count + excluded.correct_count,
                partial_count = partial_count + excluded.partial_count,
                incorrect_count = incorrect_count + excluded.incorrect_count,
                blank_count = blank_count + excluded.blank_count,
                ungradable_count = ungradable_count + excluded.ungradable_count,
                score_sum = score_sum + excluded.score_sum,
                score_max_sum = score_max_sum + excluded.score_max_sum";

        foreach (var block in blocks)
        {
            var counters = block.Outcome switch
            {
                "Correct" => new BlockCounters(1, 0, 0, 0, 0),
                "Partial" => new BlockCounters(0, 1, 0, 0, 0),
                "Incorrect" => new BlockCounters(0, 0, 1, 0, 0),
                "Blank" => new BlockCounters(0, 0, 0, 1, 0),
                "Ungradable" => new BlockCounters(0, 0, 0, 0, 1),
                _ => new BlockCounters(0, 0, 0, 0, 0),
            };

            await connection.ExecuteAsync(new CommandDefinition(
                upsertBlockSql,
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    RollupId = rollupId,
                    BlockId = block.BlockId,
                    Correct = counters.Correct,
                    Partial = counters.Partial,
                    Incorrect = counters.Incorrect,
                    Blank = counters.Blank,
                    Ungradable = counters.Ungradable,
                    Score = block.Score,
                    ScoreMax = block.ScoreMax,
                },
                cancellationToken: cancellationToken));
        }
    }

    public async Task SelfHealIfInconsistentAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        const string gradedCountSql = @"
            SELECT COUNT(*)
            FROM student_attempts sa
            JOIN delivery_sessions ds ON ds.id = sa.delivery_session_id
            JOIN attempt_results ar
                   ON ar.student_attempt_id = sa.id
                  AND ar.grading_schema_version = (SELECT MAX(grading_schema_version)
                                                   FROM attempt_results
                                                  WHERE student_attempt_id = sa.id)
            WHERE ds.school_year IS NOT NULL
              AND ds.roster_section_id IS NOT NULL
              AND ar.status = 'graded'
              AND ar.blocks_json IS NOT NULL";

        const string rollupTotalSql = "SELECT COALESCE(SUM(attempt_count), 0) FROM stats_rollups";

        var gradedCount = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(gradedCountSql, cancellationToken: cancellationToken));
        var rollupTotal = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(rollupTotalSql, cancellationToken: cancellationToken));

        if (gradedCount == rollupTotal)
        {
            return;
        }

        _logger.LogWarning(
            "Stats rollup drift detected on startup: {GradedCount} graded attempts vs {RollupTotal} counted in stats_rollups. Rebuilding.",
            gradedCount,
            rollupTotal);

        await RebuildAllAsync(cancellationToken);
    }

    public async Task RebuildTupleAsync(string cue, string schoolYear, string course, string examVersionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        await RebuildTupleCoreAsync(connection, cue, schoolYear, course, examVersionId, cancellationToken);
    }

    public async Task RebuildAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        const string tuplesSql = @"
            SELECT DISTINCT
                   ds.school_code        AS Cue,
                   ds.school_year        AS SchoolYear,
                   COALESCE(lrs.course, '') AS Course,
                   ds.exam_version_id    AS ExamVersionId
            FROM student_attempts sa
            JOIN delivery_sessions ds ON ds.id = sa.delivery_session_id
            LEFT JOIN local_roster_sections lrs ON lrs.id = ds.roster_section_id
            LEFT JOIN attempt_results ar
                   ON ar.student_attempt_id = sa.id
                  AND ar.grading_schema_version = (SELECT MAX(grading_schema_version)
                                                     FROM attempt_results
                                                    WHERE student_attempt_id = sa.id)
            WHERE ds.school_year IS NOT NULL
              AND ds.roster_section_id IS NOT NULL
              AND ar.status = 'graded'
              AND ar.blocks_json IS NOT NULL";

        var tuples = await connection.QueryAsync<TupleRow>(
            new CommandDefinition(tuplesSql, cancellationToken: cancellationToken));

        foreach (var tuple in tuples)
        {
            var course = string.IsNullOrEmpty(tuple.Course) ? UnassignedCourse : tuple.Course;
            await RebuildTupleCoreAsync(connection, tuple.Cue, tuple.SchoolYear, course, tuple.ExamVersionId, cancellationToken);
        }
    }

    private async Task RebuildTupleCoreAsync(SqliteConnection connection, string cue, string schoolYear, string course, string examVersionId, CancellationToken cancellationToken)
    {
        const string attemptsSql = @"
            SELECT ar.score              AS Score,
                   ar.score_max          AS ScoreMax,
                   ar.blocks_json        AS BlocksJson
            FROM student_attempts sa
            JOIN delivery_sessions ds ON ds.id = sa.delivery_session_id
            LEFT JOIN local_roster_sections lrs ON lrs.id = ds.roster_section_id
            LEFT JOIN attempt_results ar
                   ON ar.student_attempt_id = sa.id
                  AND ar.grading_schema_version = (SELECT MAX(grading_schema_version)
                                                     FROM attempt_results
                                                    WHERE student_attempt_id = sa.id)
            WHERE ds.school_code = @Cue
              AND ds.school_year = @SchoolYear
              AND ds.exam_version_id = @ExamVersionId
              AND COALESCE(NULLIF(lrs.course, ''), @UnassignedCourse) = @Course
              AND ar.status = 'graded'
              AND ar.blocks_json IS NOT NULL";

        var attempts = (await connection.QueryAsync<AttemptRow>(
            new CommandDefinition(
                attemptsSql,
                new { Cue = cue, SchoolYear = schoolYear, Course = course, ExamVersionId = examVersionId, UnassignedCourse = UnassignedCourse },
                cancellationToken: cancellationToken))).ToList();

        const string deleteRollupSql = @"
            DELETE FROM stats_rollups
            WHERE cue = @Cue AND school_year = @SchoolYear AND course = @Course AND exam_version_id = @ExamVersionId";

        await connection.ExecuteAsync(new CommandDefinition(
            deleteRollupSql,
            new { Cue = cue, SchoolYear = schoolYear, Course = course, ExamVersionId = examVersionId },
            cancellationToken: cancellationToken));

        if (attempts.Count == 0)
        {
            return;
        }

        var rollupId = Guid.NewGuid().ToString();
        var updatedAt = DateTimeOffset.UtcNow.ToString("O");
        var scoreSum = attempts.Sum(attempt => attempt.Score);
        var scoreMaxSum = attempts.Sum(attempt => attempt.ScoreMax);

        const string insertRollupSql = @"
            INSERT INTO stats_rollups (id, cue, school_year, course, exam_version_id, attempt_count, score_sum, score_max_sum, updated_at)
            VALUES (@Id, @Cue, @SchoolYear, @Course, @ExamVersionId, @AttemptCount, @ScoreSum, @ScoreMaxSum, @UpdatedAt)";

        await connection.ExecuteAsync(new CommandDefinition(
            insertRollupSql,
            new
            {
                Id = rollupId,
                Cue = cue,
                SchoolYear = schoolYear,
                Course = course,
                ExamVersionId = examVersionId,
                AttemptCount = attempts.Count,
                ScoreSum = scoreSum,
                ScoreMaxSum = scoreMaxSum,
                UpdatedAt = updatedAt,
            },
            cancellationToken: cancellationToken));

        var blockAggregates = new Dictionary<string, BlockAggregate>();

        foreach (var attempt in attempts)
        {
            var blocks = JsonSerializer.Deserialize<BlockOutcomeRow[]>(attempt.BlocksJson);
            if (blocks is null)
            {
                continue;
            }

            foreach (var block in blocks)
            {
                if (!blockAggregates.TryGetValue(block.BlockId, out var aggregate))
                {
                    aggregate = new BlockAggregate();
                    blockAggregates[block.BlockId] = aggregate;
                }

                var counters = block.Outcome switch
                {
                    "Correct" => new BlockCounters(1, 0, 0, 0, 0),
                    "Partial" => new BlockCounters(0, 1, 0, 0, 0),
                    "Incorrect" => new BlockCounters(0, 0, 1, 0, 0),
                    "Blank" => new BlockCounters(0, 0, 0, 1, 0),
                    "Ungradable" => new BlockCounters(0, 0, 0, 0, 1),
                    _ => new BlockCounters(0, 0, 0, 0, 0),
                };

                aggregate.Correct += counters.Correct;
                aggregate.Partial += counters.Partial;
                aggregate.Incorrect += counters.Incorrect;
                aggregate.Blank += counters.Blank;
                aggregate.Ungradable += counters.Ungradable;
                aggregate.ScoreSum += block.Score;
                aggregate.ScoreMaxSum += block.ScoreMax;
            }
        }

        const string insertBlockSql = @"
            INSERT INTO stats_rollup_blocks (id, rollup_id, block_id, correct_count, partial_count, incorrect_count, blank_count, ungradable_count, score_sum, score_max_sum)
            VALUES (@Id, @RollupId, @BlockId, @Correct, @Partial, @Incorrect, @Blank, @Ungradable, @ScoreSum, @ScoreMaxSum)";

        foreach (var blockAggregate in blockAggregates)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                insertBlockSql,
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    RollupId = rollupId,
                    BlockId = blockAggregate.Key,
                    Correct = blockAggregate.Value.Correct,
                    Partial = blockAggregate.Value.Partial,
                    Incorrect = blockAggregate.Value.Incorrect,
                    Blank = blockAggregate.Value.Blank,
                    Ungradable = blockAggregate.Value.Ungradable,
                    ScoreSum = blockAggregate.Value.ScoreSum,
                    ScoreMaxSum = blockAggregate.Value.ScoreMaxSum,
                },
                cancellationToken: cancellationToken));
        }
    }

    private sealed record AttemptTupleRow(
        string SchoolCode,
        string SchoolYear,
        string RosterSectionId,
        string ExamVersionId,
        string Course,
        string Status,
        double Score,
        double ScoreMax,
        string BlocksJson);

    private sealed record BlockOutcomeRow(string BlockId, string Outcome, decimal Score, decimal ScoreMax);

    private sealed record BlockCounters(int Correct, int Partial, int Incorrect, int Blank, int Ungradable);

    private sealed record TupleRow(string Cue, string SchoolYear, string Course, string ExamVersionId);

    private sealed record AttemptRow(double Score, double ScoreMax, string BlocksJson);

    private sealed class BlockAggregate
    {
        public int Correct { get; set; }
        public int Partial { get; set; }
        public int Incorrect { get; set; }
        public int Blank { get; set; }
        public int Ungradable { get; set; }
        public decimal ScoreSum { get; set; }
        public decimal ScoreMaxSum { get; set; }
    }
}
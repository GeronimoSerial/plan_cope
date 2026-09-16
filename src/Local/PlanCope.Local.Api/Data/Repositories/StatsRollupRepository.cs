using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PlanCope.Local.Api.Data;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class StatsRollupRepository : IStatsRollupRepository
{
    private const string UnassignedCourse = "sin_asignar";

    private readonly ILocalSqliteConnectionFactory _connectionFactory;

    public StatsRollupRepository(ILocalSqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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
}
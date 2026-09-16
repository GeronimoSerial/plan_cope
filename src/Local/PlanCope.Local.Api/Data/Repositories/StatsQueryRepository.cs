using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using PlanCope.Local.Api.Data;
using PlanCope.Shared.Domain;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class StatsQueryRepository : IStatsQueryRepository
{
    private readonly ILocalSqliteConnectionFactory _connectionFactory;

    public StatsQueryRepository(ILocalSqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SchoolStatsDto> GetSchoolStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var sql = @"
            SELECT COALESCE(SUM(attempt_count), 0)   AS AttemptCount,
                   COALESCE(SUM(score_sum), 0)       AS ScoreSum,
                   COALESCE(SUM(score_max_sum), 0)   AS ScoreMaxSum
            FROM stats_rollups
            WHERE " + BuildRollupsFilters(schoolYear, course);

        var row = await connection.QuerySingleAsync<RollupTotalsRow>(
            new CommandDefinition(sql, BuildRollupsParams(cue, schoolYear, course), cancellationToken: cancellationToken));

        var attemptCount = row.AttemptCount;
        var averageScorePercent = row.ScoreMaxSum > 0 ? row.ScoreSum / row.ScoreMaxSum * 100 : 0;

        return new SchoolStatsDto(
            SuppressibleValue<int>.For(rosterScope, attemptCount, attemptCount),
            SuppressibleValue<double>.For(rosterScope, attemptCount, averageScorePercent));
    }

    public async Task<IReadOnlyList<CourseStatsDto>> GetCourseStatsAsync(string cue, string rosterScope, string? schoolYear, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var sql = @"
            SELECT course               AS Course,
                   SUM(attempt_count)   AS AttemptCount,
                   SUM(score_sum)       AS ScoreSum,
                   SUM(score_max_sum)   AS ScoreMaxSum
            FROM stats_rollups
            WHERE " + BuildRollupsFilters(schoolYear, course: null) + @"
            GROUP BY course
            ORDER BY course";

        var rows = await connection.QueryAsync<CourseStatsRow>(
            new CommandDefinition(sql, BuildRollupsParams(cue, schoolYear, null), cancellationToken: cancellationToken));

        var result = new List<CourseStatsDto>();
        foreach (var row in rows)
        {
            var averageScorePercent = row.ScoreMaxSum > 0 ? row.ScoreSum / row.ScoreMaxSum * 100 : 0;

            result.Add(new CourseStatsDto(
                row.Course,
                SuppressibleValue<int>.For(rosterScope, row.AttemptCount, row.AttemptCount),
                SuppressibleValue<double>.For(rosterScope, row.AttemptCount, averageScorePercent)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ExamStatsDto>> GetExamStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var sql = @"
            SELECT sr.exam_version_id    AS ExamVersionId,
                   lev.exam_code         AS ExamCode,
                   lev.version_number    AS VersionNumber,
                   SUM(sr.attempt_count) AS AttemptCount,
                   SUM(sr.score_sum)     AS ScoreSum,
                   SUM(sr.score_max_sum) AS ScoreMaxSum
            FROM stats_rollups sr
            JOIN local_exam_versions lev ON lev.id = sr.exam_version_id
            WHERE " + BuildRollupsFilters(schoolYear, course) + @"
            GROUP BY sr.exam_version_id
            ORDER BY lev.exam_code, lev.version_number";

        var rows = await connection.QueryAsync<ExamStatsRow>(
            new CommandDefinition(sql, BuildRollupsParams(cue, schoolYear, course), cancellationToken: cancellationToken));

        var result = new List<ExamStatsDto>();
        foreach (var row in rows)
        {
            var blocksSql = @"
                SELECT rb.block_id            AS BlockId,
                       SUM(rb.correct_count)    AS CorrectCount,
                       SUM(rb.partial_count)    AS PartialCount,
                       SUM(rb.incorrect_count)  AS IncorrectCount,
                       SUM(rb.blank_count)      AS BlankCount,
                       SUM(rb.ungradable_count) AS UngradableCount
                FROM stats_rollup_blocks rb
                JOIN stats_rollups sr ON sr.id = rb.rollup_id
                WHERE " + BuildRollupsFilters(schoolYear, course) + @"
                  AND sr.exam_version_id = @ExamVersionId
                GROUP BY rb.block_id
                ORDER BY rb.block_id";

            var blocks = (await connection.QueryAsync<BlockStatRow>(
                new CommandDefinition(
                    blocksSql,
                    new
                    {
                        Cue = cue,
                        SchoolYear = schoolYear,
                        Course = course,
                        ExamVersionId = row.ExamVersionId,
                    },
                    cancellationToken: cancellationToken))).ToList();

            var blockDtos = blocks
                .Select(block => new BlockStatDto(
                    block.BlockId,
                    block.CorrectCount,
                    block.PartialCount,
                    block.IncorrectCount,
                    block.BlankCount,
                    block.UngradableCount))
                .ToList();

            var attemptCount = row.AttemptCount;
            var averageScorePercent = row.ScoreMaxSum > 0 ? row.ScoreSum / row.ScoreMaxSum * 100 : 0;

            result.Add(new ExamStatsDto(
                row.ExamVersionId,
                row.ExamCode,
                row.VersionNumber,
                SuppressibleValue<int>.For(rosterScope, attemptCount, attemptCount),
                SuppressibleValue<double>.For(rosterScope, attemptCount, averageScorePercent),
                blockDtos));
        }

        return result;
    }

    private static string BuildRollupsFilters(string? schoolYear, string? course)
    {
        var filters = "cue = @Cue";
        if (schoolYear is not null)
        {
            filters += " AND school_year = @SchoolYear";
        }

        if (course is not null)
        {
            filters += " AND course = @Course";
        }

        return filters;
    }

    private static object BuildRollupsParams(string cue, string? schoolYear, string? course) =>
        new { Cue = cue, SchoolYear = schoolYear, Course = course };

    private sealed record RollupTotalsRow(int AttemptCount, double ScoreSum, double ScoreMaxSum);

    private sealed record CourseStatsRow(string Course, int AttemptCount, double ScoreSum, double ScoreMaxSum);

    private sealed record ExamStatsRow(string ExamVersionId, string ExamCode, int VersionNumber, int AttemptCount, double ScoreSum, double ScoreMaxSum);

    private sealed record BlockStatRow(string BlockId, int CorrectCount, int PartialCount, int IncorrectCount, int BlankCount, int UngradableCount);
}
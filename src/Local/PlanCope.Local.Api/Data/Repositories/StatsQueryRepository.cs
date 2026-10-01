using System.Collections.Generic;
using System.Text.Json;
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
            SELECT COALESCE(SUM(attempt_count), 0)     AS AttemptCount,
                   COALESCE(SUM(score_sum), 0.0)       AS ScoreSum,
                   COALESCE(SUM(score_max_sum), 0.0)   AS ScoreMaxSum
            FROM stats_rollups
            WHERE " + BuildRollupsFilters(schoolYear, course);

        var row = await connection.QuerySingleAsync<RollupTotalsRow>(
            new CommandDefinition(sql, BuildRollupsParams(cue, schoolYear, course), cancellationToken: cancellationToken));

        var attemptCount = (int)row.AttemptCount;
        var averageScorePercent = row.ScoreMaxSum > 0 ? Math.Clamp(row.ScoreSum / row.ScoreMaxSum * 100, 0, 100) : 0;

        return new SchoolStatsDto(
            SuppressibleValue<int>.For(rosterScope, attemptCount, attemptCount),
            SuppressibleValue<double>.For(rosterScope, attemptCount, averageScorePercent));
    }

    public async Task<IReadOnlyList<CourseStatsDto>> GetCourseStatsAsync(string cue, string rosterScope, string? schoolYear, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var courseIds = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT course FROM stats_rollups WHERE " + BuildRollupsFilters(schoolYear, course: null),
            BuildRollupsParams(cue, schoolYear, null), cancellationToken: cancellationToken))).ToList();

        if (courseIds.Count == 0)
        {
            return new List<CourseStatsDto>();
        }

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

        var sectionRows = await connection.QueryAsync<CourseDivisionRow>(new CommandDefinition("""
            SELECT DISTINCT COALESCE(NULLIF(rs.course, ''), 'sin_asignar') AS Course,
                   NULLIF(TRIM(rs.division), '') AS Division
            FROM student_attempts a
            JOIN delivery_sessions ds ON ds.id = a.delivery_session_id
            LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id
            WHERE ds.school_code = @Cue AND a.status = 'submitted' AND a.submitted_at IS NOT NULL
              AND (@SchoolYear IS NULL OR ds.school_year = @SchoolYear)
              AND NULLIF(TRIM(rs.division), '') IS NOT NULL
            """, new { Cue = cue, SchoolYear = schoolYear }, cancellationToken: cancellationToken));
        var sectionsByCourse = sectionRows.GroupBy(row => row.Course)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(row => row.Division).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<CourseStatsDto>();
        foreach (var row in rows)
        {
            var averageScorePercent = row.ScoreMaxSum > 0 ? Math.Clamp(row.ScoreSum / row.ScoreMaxSum * 100, 0, 100) : 0;

            result.Add(new CourseStatsDto(
                row.Course,
                SuppressibleValue<int>.For(rosterScope, (int)row.AttemptCount, (int)row.AttemptCount),
                SuppressibleValue<double>.For(rosterScope, (int)row.AttemptCount, averageScorePercent),
                sectionsByCourse.GetValueOrDefault(row.Course, Array.Empty<string>())));
        }

        return result;
    }

    public async Task<IReadOnlyList<ExamStatsDto>> GetExamStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var examVersionIds = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT sr.exam_version_id FROM stats_rollups sr JOIN local_exam_versions lev ON lev.id = sr.exam_version_id WHERE " + BuildRollupsFilters(schoolYear, course),
            BuildRollupsParams(cue, schoolYear, course), cancellationToken: cancellationToken))).ToList();

        if (examVersionIds.Count == 0)
        {
            return new List<ExamStatsDto>();
        }

        var sql = @"
            SELECT sr.exam_version_id    AS ExamVersionId,
                   lev.exam_code         AS ExamCode,
                   lev.metadata_json    AS MetadataJson,
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

        var examCourses = (await connection.QueryAsync<ExamCourseRow>(new CommandDefinition(
            "SELECT DISTINCT exam_version_id AS ExamVersionId, course AS Course FROM stats_rollups WHERE " + BuildRollupsFilters(schoolYear, course),
            BuildRollupsParams(cue, schoolYear, course), cancellationToken: cancellationToken)))
            .GroupBy(item => item.ExamVersionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(item => item.Course).OrderBy(item => item, StringComparer.Ordinal).ToList());
        var examSections = (await connection.QueryAsync<ExamSectionRow>(new CommandDefinition("""
            SELECT DISTINCT ds.exam_version_id AS ExamVersionId,
                   COALESCE(NULLIF(rs.course, ''), 'sin_asignar') AS Course,
                   NULLIF(TRIM(rs.division), '') AS Division,
                   NULLIF(TRIM(rs.shift), '') AS Shift
            FROM student_attempts a
            JOIN delivery_sessions ds ON ds.id = a.delivery_session_id
            LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id AND rs.snapshot_id = ds.roster_snapshot_id
            WHERE ds.school_code = @Cue AND a.status = 'submitted' AND a.submitted_at IS NOT NULL
              AND (@SchoolYear IS NULL OR ds.school_year = @SchoolYear)
              AND (@Course IS NULL OR COALESCE(NULLIF(rs.course, ''), 'sin_asignar') = @Course)
              AND NULLIF(TRIM(rs.division), '') IS NOT NULL
            """, new { Cue = cue, SchoolYear = schoolYear, Course = course }, cancellationToken: cancellationToken)))
            .GroupBy(item => item.ExamVersionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ExamSectionDto>)group
                .Select(item => new ExamSectionDto(item.Course, item.Division, item.Shift))
                .OrderBy(item => item.Course, StringComparer.Ordinal).ThenBy(item => item.Division, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Shift, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<ExamStatsDto>();
        foreach (var row in rows)
        {
            var blocksSql = @"
                SELECT rb.block_id            AS BlockId,
                       COALESCE(leb.order_index, -1) AS OrderIndex,
                       leb.config_json          AS ConfigJson,
                       SUM(rb.correct_count)    AS CorrectCount,
                       SUM(rb.partial_count)    AS PartialCount,
                       SUM(rb.incorrect_count)  AS IncorrectCount,
                       SUM(rb.blank_count)      AS BlankCount,
                       SUM(rb.ungradable_count) AS UngradableCount
                FROM stats_rollup_blocks rb
                JOIN stats_rollups sr ON sr.id = rb.rollup_id
                LEFT JOIN local_exam_blocks leb
                  ON leb.local_exam_version_id = sr.exam_version_id
                 AND (leb.id = rb.block_id OR leb.remote_block_id = rb.block_id)
                WHERE " + BuildRollupsFilters(schoolYear, course) + @"
                  AND sr.exam_version_id = @ExamVersionId
                GROUP BY rb.block_id, leb.order_index, leb.config_json
                ORDER BY leb.order_index, rb.block_id";

            var blockIds = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT DISTINCT rb.block_id FROM stats_rollup_blocks rb JOIN stats_rollups sr ON sr.id = rb.rollup_id WHERE " + BuildRollupsFilters(schoolYear, course) + " AND sr.exam_version_id = @ExamVersionId",
                new { Cue = cue, SchoolYear = schoolYear, Course = course, ExamVersionId = row.ExamVersionId },
                cancellationToken: cancellationToken))).ToList();

            var blockDtos = blockIds.Count == 0
                ? new List<BlockStatDto>()
                : (await connection.QueryAsync<BlockStatRow>(new CommandDefinition(
                      blocksSql,
                      new { Cue = cue, SchoolYear = schoolYear, Course = course, ExamVersionId = row.ExamVersionId },
                      cancellationToken: cancellationToken)))
                  .Select(block => new BlockStatDto(
                      block.BlockId, block.OrderIndex >= 0 ? checked((int)block.OrderIndex) : null, ExtractBlockTitle(block.ConfigJson),
                      (int)block.CorrectCount, (int)block.PartialCount,
                      (int)block.IncorrectCount, (int)block.BlankCount, (int)block.UngradableCount))
                  .ToList();

            var attemptCount = (int)row.AttemptCount;
            var averageScorePercent = row.ScoreMaxSum > 0 ? Math.Clamp(row.ScoreSum / row.ScoreMaxSum * 100, 0, 100) : 0;

            result.Add(new ExamStatsDto(
                row.ExamVersionId,
                row.ExamCode,
                (int)row.VersionNumber,
                SuppressibleValue<int>.For(rosterScope, attemptCount, attemptCount),
                SuppressibleValue<double>.For(rosterScope, attemptCount, averageScorePercent),
                blockDtos,
                ReadExamTitle(row.MetadataJson),
                examCourses.GetValueOrDefault(row.ExamVersionId, Array.Empty<string>()),
                examSections.GetValueOrDefault(row.ExamVersionId, Array.Empty<ExamSectionDto>())));
        }

        return result;
    }

    public async Task<StatsReportDataDto> GetReportDataAsync(string cue, string? schoolYear, string? course, string? examVersionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();

        var schoolName = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT COALESCE(name, cue) FROM schools WHERE cue = @Cue", new { Cue = cue }, cancellationToken: cancellationToken)) ?? cue;

        var filters = "ds.school_code = @Cue AND a.status = 'submitted' AND a.submitted_at IS NOT NULL";
        if (schoolYear is not null) filters += " AND ds.school_year = @SchoolYear";
        if (course is not null) filters += " AND COALESCE(NULLIF(rs.course, ''), 'sin_asignar') = @Course";
        if (examVersionId is not null) filters += " AND ds.exam_version_id = @ExamVersionId";

        var args = new { Cue = cue, SchoolYear = schoolYear, Course = course, ExamVersionId = examVersionId };
        var hasAttempts = await connection.ExecuteScalarAsync<bool>(new CommandDefinition($@"
            SELECT EXISTS (
                SELECT 1 FROM student_attempts a
                JOIN delivery_sessions ds ON ds.id = a.delivery_session_id
                JOIN local_exam_versions ev ON ev.id = ds.exam_version_id
                LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id
                WHERE {filters})", args, cancellationToken: cancellationToken));
        var rows = hasAttempts
            ? await connection.QueryAsync<ReportAttemptRow>(new CommandDefinition($@"
            SELECT a.student_first_name AS FirstName,
                   a.student_last_name AS LastName,
                   a.document_last4 AS DocumentLast4,
                   CAST(COALESCE(NULLIF(rs.course, ''), 'Sin asignar') AS TEXT) AS Course,
                   CAST(TRIM(COALESCE(rs.division, '') || CASE WHEN rs.division IS NOT NULL AND rs.shift IS NOT NULL THEN ' · ' ELSE '' END || COALESCE(rs.shift, '')) AS TEXT) AS Section,
                   ds.exam_version_id AS ExamVersionId,
                   ev.exam_code AS ExamCode,
                   CAST(CASE WHEN ar.score_max > 0 THEN ar.score * 100.0 / ar.score_max ELSE 0.0 END AS REAL) AS ScorePercent,
                   CASE WHEN ar.score_max > 0 THEN 1 ELSE 0 END AS HasScore,
                   a.started_at AS StartedAt,
                   a.submitted_at AS SubmittedAt
            FROM student_attempts a
            JOIN delivery_sessions ds ON ds.id = a.delivery_session_id
            JOIN local_exam_versions ev ON ev.id = ds.exam_version_id
            LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id
            LEFT JOIN attempt_results ar ON ar.id = (
                SELECT result.id FROM attempt_results result
                WHERE result.student_attempt_id = a.id AND result.status = 'graded'
                ORDER BY result.graded_at DESC LIMIT 1)
            WHERE {filters}
            ORDER BY ev.exam_code, rs.course, rs.division, ar.score, a.student_last_name, a.student_first_name", args, cancellationToken: cancellationToken))
            : Array.Empty<ReportAttemptRow>();

        var expectedFilters = "ds.school_code = @Cue";
        if (schoolYear is not null) expectedFilters += " AND ds.school_year = @SchoolYear";
        if (course is not null) expectedFilters += " AND COALESCE(NULLIF(rs.course, ''), 'sin_asignar') = @Course";
        if (examVersionId is not null) expectedFilters += " AND ds.exam_version_id = @ExamVersionId";
        var deliveredExams = await connection.ExecuteScalarAsync<long>(new CommandDefinition($@"
            SELECT COUNT(DISTINCT ds.exam_version_id)
            FROM delivery_sessions ds
            LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id
            WHERE {expectedFilters}", args, cancellationToken: cancellationToken));
        var expected = await connection.ExecuteScalarAsync<long>(new CommandDefinition($@"
            SELECT COALESCE(SUM(ds.expected_student_count), 0)
            FROM delivery_sessions ds
            LEFT JOIN local_roster_sections rs ON rs.id = ds.roster_section_id
            WHERE {expectedFilters}", args, cancellationToken: cancellationToken));

        var attempts = rows.Select(row => new StatsReportAttemptDto(
            string.Join(" ", new[] { row.FirstName, row.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
            row.DocumentLast4,
            row.Course,
            row.Section,
            row.ExamVersionId,
            row.ExamCode,
            row.HasScore == 1 ? Math.Clamp(row.ScorePercent, 0, 100) : null,
            ParseDate(row.StartedAt),
            ParseDate(row.SubmittedAt))).ToList();

        return new StatsReportDataDto(cue, schoolName, attempts, (int)Math.Min(deliveredExams, int.MaxValue), (int)Math.Min(expected, int.MaxValue));
    }

    public async Task<bool> HasSubmittedAttemptsAsync(string cue, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM student_attempts a
                JOIN delivery_sessions ds ON ds.id = a.delivery_session_id
                WHERE ds.school_code = @Cue AND a.status = 'submitted' AND a.submitted_at IS NOT NULL)
            """, new { Cue = cue }, cancellationToken: cancellationToken));
    }

    public async Task<StatsFilterOptionsDto> GetReportFilterOptionsAsync(string cue, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var years = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT school_year FROM delivery_sessions WHERE school_code = @Cue AND school_year IS NOT NULL ORDER BY school_year DESC",
            new { Cue = cue }, cancellationToken: cancellationToken))).ToList();
        var courses = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT course FROM stats_rollups WHERE cue = @Cue ORDER BY course",
            new { Cue = cue }, cancellationToken: cancellationToken))).ToList();
        var sections = (await connection.QueryAsync<StatsFilterSectionOption>(new CommandDefinition("""
            SELECT DISTINCT rs.course AS Course, rs.division AS Division, NULLIF(TRIM(rs.shift), '') AS Shift
            FROM delivery_sessions ds
            JOIN local_roster_sections rs ON rs.id = ds.roster_section_id AND rs.snapshot_id = ds.roster_snapshot_id
            JOIN student_attempts a ON a.delivery_session_id = ds.id
            WHERE ds.school_code = @Cue AND a.status = 'submitted' AND a.submitted_at IS NOT NULL
              AND NULLIF(TRIM(rs.course), '') IS NOT NULL AND NULLIF(TRIM(rs.division), '') IS NOT NULL
            ORDER BY rs.course, rs.division, rs.shift
            """, new { Cue = cue }, cancellationToken: cancellationToken))).ToList();
        var exams = (await connection.QueryAsync<ExamFilterOptionDto>(new CommandDefinition(@"
            SELECT DISTINCT ev.id AS ExamVersionId, ev.exam_code AS ExamCode, ev.version_number AS VersionNumber
            FROM delivery_sessions ds JOIN local_exam_versions ev ON ev.id = ds.exam_version_id
            WHERE ds.school_code = @Cue ORDER BY ev.exam_code, ev.version_number",
            new { Cue = cue }, cancellationToken: cancellationToken))).ToList();
        return new StatsFilterOptionsDto(years, courses, exams, sections);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

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

    private sealed class RollupTotalsRow
    {
        public long AttemptCount { get; set; }
        public double ScoreSum { get; set; }
        public double ScoreMaxSum { get; set; }
    }

    private sealed class CourseStatsRow
    {
        public string Course { get; set; } = string.Empty;
        public long AttemptCount { get; set; }
        public double ScoreSum { get; set; }
        public double ScoreMaxSum { get; set; }
    }

    private sealed class ExamStatsRow
    {
        public string ExamVersionId { get; set; } = string.Empty;
        public string ExamCode { get; set; } = string.Empty;
        public string? MetadataJson { get; set; }
        public long VersionNumber { get; set; }
        public long AttemptCount { get; set; }
        public double ScoreSum { get; set; }
        public double ScoreMaxSum { get; set; }
    }

    private sealed class ExamCourseRow
    {
        public string ExamVersionId { get; set; } = string.Empty;
        public string Course { get; set; } = string.Empty;
    }

    private sealed class CourseDivisionRow
    {
        public string Course { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
    }

    private sealed class ExamSectionRow
    {
        public string ExamVersionId { get; set; } = string.Empty;
        public string Course { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public string? Shift { get; set; }
    }

    private static string? ReadExamTitle(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? title.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ExtractBlockTitle(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var propertyName in new[] { "question", "prompt", "title" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Older or malformed exam content can still contribute statistics without a title.
        }

        return null;
    }

    private sealed class BlockStatRow
    {
        public string BlockId { get; set; } = string.Empty;
        public long OrderIndex { get; set; } = -1;
        public string? ConfigJson { get; set; }
        public long CorrectCount { get; set; }
        public long PartialCount { get; set; }
        public long IncorrectCount { get; set; }
        public long BlankCount { get; set; }
        public long UngradableCount { get; set; }
    }

    private sealed class ReportAttemptRow
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DocumentLast4 { get; set; }
        public string Course { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string ExamVersionId { get; set; } = string.Empty;
        public string ExamCode { get; set; } = string.Empty;
        public double ScorePercent { get; set; }
        public long HasScore { get; set; }
        public string? StartedAt { get; set; }
        public string? SubmittedAt { get; set; }
    }
}

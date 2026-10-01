using Dapper;
using System.Globalization;
using System.Text.Json;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class SessionRepository(ILocalSqliteConnectionFactory connectionFactory) : ISessionRepository
{
    public async Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id)
            VALUES (@Id, @ExamVersionId, @SchoolCode, @ClassroomCode, @CommissionCode, @StartedBy, @StartAt, @EndAt, @Status, @ConfigJson, @AccessCode, @ExpectedStudentCount, @SchoolYear, @RosterSnapshotId, @RosterSectionId);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        await Schools.EnsureRowAsync(connection, transaction, session.SchoolCode, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, session, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
    }

    public async Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id
            FROM delivery_sessions
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalDeliverySessionRow>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<LocalDeliverySession?> GetByIdOrAccessCodeAsync(string idOrAccessCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id
            FROM delivery_sessions
            WHERE id = @IdOrAccessCode
               OR access_code = @AccessCode
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalDeliverySessionRow>(new CommandDefinition(sql, new { IdOrAccessCode = idOrAccessCode, AccessCode = idOrAccessCode.ToUpperInvariant() }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<bool> AccessCodeExistsAsync(string accessCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM delivery_sessions
                WHERE access_code = @AccessCode
            );
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { AccessCode = accessCode.ToUpperInvariant() }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LocalDeliverySession>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id
            FROM delivery_sessions
            WHERE status IN ('active', 'paused')
            ORDER BY start_at DESC;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var sessions = await connection.QueryAsync<LocalDeliverySessionRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return sessions.Select(static row => row.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<SessionListItem>> GetActiveSummariesAsync(string? schoolCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.id AS Id, s.exam_version_id AS ExamVersionId, s.school_code AS SchoolCode,
                   s.roster_snapshot_id AS RosterSnapshotId, s.roster_section_id AS RosterSectionId,
                   COALESCE(NULLIF(sc.name, ''), NULLIF(rs.school_name, ''), 'CUE ' || s.school_code) AS SchoolName,
                   ev.exam_code AS ExamCode, ev.metadata_json AS MetadataJson,
                   COALESCE(section.course, json_extract(ev.metadata_json, '$.grade')) AS Course,
                   COALESCE(section.division, json_extract(ev.metadata_json, '$.division')) AS Division,
                   section.shift AS Shift, s.start_at AS StartAt, s.end_at AS EndAt, s.status AS Status,
                   s.access_code AS AccessCode, s.expected_student_count AS ExpectedStudentCount,
                   (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'submitted') AS SubmittedCount,
                   (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'in_progress') AS InProgressCount
            FROM delivery_sessions s
            JOIN local_exam_versions ev ON ev.id = s.exam_version_id
            LEFT JOIN schools sc ON sc.cue = s.school_code
            LEFT JOIN local_roster_snapshots rs ON rs.id = s.roster_snapshot_id
            LEFT JOIN local_roster_sections section ON section.id = s.roster_section_id AND section.snapshot_id = s.roster_snapshot_id
            WHERE s.status IN ('active', 'paused') AND (@SchoolCode IS NULL OR s.school_code = @SchoolCode)
            ORDER BY s.start_at DESC;
            """;
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<SessionListRow>(new CommandDefinition(sql,
            new { SchoolCode = NormalizeOptionalCue(schoolCode) }, cancellationToken: cancellationToken));
        return rows.Select(ToListItem).ToList();
    }

    public async Task<SessionHistoryPage> GetHistoryAsync(string? schoolCode, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH filtered AS (
                SELECT s.id AS Id, s.exam_version_id AS ExamVersionId, s.school_code AS SchoolCode,
                       s.roster_snapshot_id AS RosterSnapshotId, s.roster_section_id AS RosterSectionId,
                       COALESCE(NULLIF(sc.name, ''), NULLIF(rs.school_name, ''), 'CUE ' || s.school_code) AS SchoolName,
                       ev.exam_code AS ExamCode, ev.metadata_json AS MetadataJson,
                       COALESCE(section.course, json_extract(ev.metadata_json, '$.grade')) AS Course,
                       COALESCE(section.division, json_extract(ev.metadata_json, '$.division')) AS Division,
                       section.shift AS Shift, s.start_at AS StartAt, s.end_at AS EndAt, s.status AS Status,
                       s.access_code AS AccessCode, s.expected_student_count AS ExpectedStudentCount,
                       (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'submitted') AS SubmittedCount,
                       (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'in_progress') AS InProgressCount
                FROM delivery_sessions s
                JOIN local_exam_versions ev ON ev.id = s.exam_version_id
                LEFT JOIN schools sc ON sc.cue = s.school_code
                LEFT JOIN local_roster_snapshots rs ON rs.id = s.roster_snapshot_id
                LEFT JOIN local_roster_sections section ON section.id = s.roster_section_id AND section.snapshot_id = s.roster_snapshot_id
                WHERE (@SchoolCode IS NULL OR s.school_code = @SchoolCode)
                  AND (@Status IS NULL OR s.status = @Status)
            )
            SELECT * FROM filtered ORDER BY StartAt DESC, Id DESC LIMIT @PageSize OFFSET @Offset;
            """;
        using var connection = connectionFactory.CreateOpenConnection();
        var statusFilter = NormalizeOptionalStatus(status);
        var schoolFilter = NormalizeOptionalCue(schoolCode);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT(*) FROM delivery_sessions s
            WHERE (@SchoolCode IS NULL OR s.school_code = @SchoolCode)
              AND (@Status IS NULL OR s.status = @Status);
            {sql}
            """, new
        {
            SchoolCode = schoolFilter,
            Status = statusFilter,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        }, cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<int>();
        var rows = (await results.ReadAsync<SessionListRow>()).ToList();
        return new SessionHistoryPage(rows.Select(ToListItem).ToList(), page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<LocalSchoolListItem>> GetSchoolsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.cue AS Code, COALESCE(NULLIF(s.name, ''), NULLIF(r.school_name, ''), 'CUE ' || s.cue) AS Name,
                   EXISTS (SELECT 1 FROM local_roster_snapshots ready WHERE ready.id = r.id AND lower(ready.status) = 'ready'
                           AND EXISTS (SELECT 1 FROM local_roster_sections sec WHERE sec.snapshot_id = ready.id)) AS HasReadyRoster
            FROM schools s
            LEFT JOIN local_roster_snapshots r ON r.id = (
                SELECT snap.id FROM local_roster_snapshots snap WHERE snap.cue = s.cue ORDER BY snap.fetched_at DESC LIMIT 1)
            ORDER BY Name COLLATE NOCASE, s.cue;
            """;
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<LocalSchoolListRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(row => new LocalSchoolListItem(row.Code, row.Name, row.HasReadyRoster != 0)).ToList();
    }

    private static SessionListItem ToListItem(SessionListRow row) => new(
        row.Id, row.ExamVersionId, row.SchoolCode, row.SchoolName,
        ReadExamTitle(row.MetadataJson, row.ExamCode),
        GradeLabelFormatter.Format(row.Course, row.Division, row.Shift), row.StartAt, row.EndAt,
        row.Status, row.AccessCode, checked((int)row.ExpectedStudentCount), checked((int)row.SubmittedCount), checked((int)row.InProgressCount),
        row.RosterSnapshotId, row.RosterSectionId);

    private static string ReadExamTitle(string? metadataJson, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson ?? "{}");
            if (document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString() ?? fallback;
        }
        catch (JsonException) { }
        return fallback;
    }

    private static string? NormalizeOptionalCue(string? cue) => string.IsNullOrWhiteSpace(cue) ? null : CueCode.Normalize(cue);

    private static string? NormalizeOptionalStatus(string? status) => string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant() switch
    {
        "active" or "abierta" => "active",
        "paused" or "pausada" => "paused",
        "closed" or "cerrada" => "closed",
        _ => "invalid"
    };

    private sealed class SessionListRow
    {
        public string Id { get; init; } = "";
        public string ExamVersionId { get; init; } = "";
        public string SchoolCode { get; init; } = "";
        public string? RosterSnapshotId { get; init; }
        public string? RosterSectionId { get; init; }
        public string SchoolName { get; init; } = "";
        public string ExamCode { get; init; } = "";
        public string? MetadataJson { get; init; }
        public string? Course { get; init; }
        public string? Division { get; init; }
        public string? Shift { get; init; }
        public string StartAt { get; init; } = "";
        public string? EndAt { get; init; }
        public string Status { get; init; } = "";
        public string AccessCode { get; init; } = "";
        public long ExpectedStudentCount { get; init; }
        public long SubmittedCount { get; init; }
        public long InProgressCount { get; init; }
    }

    private sealed class LocalSchoolListRow
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public long HasReadyRoster { get; init; }
    }

    public async Task<LocalSessionProgress?> GetProgressAsync(string idOrAccessCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH session_context AS (
                SELECT
                    s.id AS session_id,
                    s.access_code,
                    s.expected_student_count,
                    s.roster_snapshot_id,
                    s.roster_section_id,
                    ev.metadata_json,
                    section.course,
                    section.division,
                    section.shift,
                    section.level,
                    (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id) AS started_count,
                    (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'submitted') AS submitted_count,
                    (SELECT COUNT(*) FROM student_attempts a WHERE a.delivery_session_id = s.id AND a.status = 'in_progress') AS in_progress_count,
                    (SELECT AVG(100.0 * r.score / r.score_max)
                     FROM student_attempts a JOIN attempt_results r ON r.student_attempt_id = a.id
                     WHERE a.delivery_session_id = s.id AND r.status = 'graded' AND r.score_max > 0) AS average_score_percent
                FROM delivery_sessions s
                LEFT JOIN local_exam_versions ev ON ev.id = s.exam_version_id
                LEFT JOIN local_roster_sections section
                    ON section.id = s.roster_section_id AND section.snapshot_id = s.roster_snapshot_id
                WHERE s.id = @IdOrAccessCode OR s.access_code = @AccessCode
                LIMIT 1
            ), progress_rows AS (
                SELECT
                    c.*,
                    COALESCE(rs.id, a.id) AS student_id,
                    COALESCE(rs.last_name, a.student_last_name, a.student_code) AS last_name,
                    COALESCE(rs.first_name, a.student_first_name) AS first_name,
                    COALESCE(rs.document_last4, a.document_last4) AS document_last4,
                    a.id AS attempt_id,
                    a.status AS attempt_status,
                    a.started_at,
                    a.submitted_at,
                    a.submission_reason,
                    0 AS off_roster
                FROM session_context c
                LEFT JOIN local_roster_students rs
                    ON c.roster_snapshot_id IS NOT NULL
                    AND c.roster_section_id IS NOT NULL
                    AND rs.snapshot_id = c.roster_snapshot_id
                    AND rs.section_id = c.roster_section_id
                LEFT JOIN student_attempts a
                    ON a.delivery_session_id = c.session_id
                    AND (
                        (rs.id IS NOT NULL AND (a.roster_student_id = rs.id OR (a.roster_student_id IS NULL AND a.ge_person_id = rs.ge_person_id)))
                        OR (rs.id IS NULL AND (c.roster_snapshot_id IS NULL OR c.roster_section_id IS NULL))
                    )
                UNION ALL
                SELECT
                    c.*,
                    a.id AS student_id,
                    COALESCE(a.student_last_name, a.student_code) AS last_name,
                    a.student_first_name AS first_name,
                    a.document_last4,
                    a.id AS attempt_id,
                    a.status AS attempt_status,
                    a.started_at,
                    a.submitted_at,
                    a.submission_reason,
                    0 AS off_roster
                FROM session_context c
                JOIN student_attempts a ON a.delivery_session_id = c.session_id
                WHERE c.roster_snapshot_id IS NOT NULL AND c.roster_section_id IS NOT NULL
                  AND a.extra_student_id IS NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM local_roster_students rs
                    WHERE rs.snapshot_id = c.roster_snapshot_id AND rs.section_id = c.roster_section_id
                      AND (a.roster_student_id = rs.id OR (a.roster_student_id IS NULL AND a.ge_person_id = rs.ge_person_id))
                  )
                UNION ALL
                SELECT
                    c.*,
                    e.id AS student_id,
                    e.last_name,
                    e.first_name,
                    e.document_last4,
                    a.id AS attempt_id,
                    a.status AS attempt_status,
                    a.started_at,
                    a.submitted_at,
                    a.submission_reason,
                    1 AS off_roster
                FROM session_context c
                JOIN session_extra_students e ON e.session_id = c.session_id
                LEFT JOIN student_attempts a ON a.extra_student_id = e.id
            )
            SELECT * FROM progress_rows
            ORDER BY last_name COLLATE NOCASE, first_name COLLATE NOCASE, student_id;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var rows = (await connection.QueryAsync<LocalSessionProgressRow>(new CommandDefinition(sql, new { IdOrAccessCode = idOrAccessCode, AccessCode = idOrAccessCode.ToUpperInvariant() }, cancellationToken: cancellationToken))).ToList();
        if (rows.Count == 0) return null;

        var first = rows[0];
        var spanishComparer = StringComparer.Create(new CultureInfo("es-AR"), CompareOptions.IgnoreCase);
        var students = rows
            .Where(static row => row.StudentId is not null)
            .OrderBy(row => row.LastName, spanishComparer)
            .ThenBy(row => row.FirstName, spanishComparer)
            .ThenBy(static row => row.StudentId, StringComparer.Ordinal)
            .Select(static row => new LocalSessionStudentProgress(
                row.StudentId!,
                FormatDisplayName(row.LastName, row.FirstName),
                MaskDocument(row.DocumentLast4),
                row.AttemptStatus ?? "not_started",
                row.StartedAt,
                row.SubmittedAt,
                row.AttemptId,
                row.SubmissionReason,
                row.OffRoster))
            .ToList();

        var course = first.Course;
        var division = first.Division;
        if (first.RosterSnapshotId is null || first.RosterSectionId is null)
        {
            (course, division) = ReadGradeFromMetadata(first.MetadataJson);
        }

        return new LocalSessionProgress(
            first.SessionId,
            first.AccessCode,
            checked((int)first.ExpectedStudentCount),
            checked((int)first.StartedCount),
            checked((int)first.SubmittedCount),
            checked((int)first.InProgressCount),
            students,
            GradeLabelFormatter.Format(course, division, first.Shift),
            course,
            division,
            first.Shift,
            first.Level,
            first.AverageScorePercent,
            first.RosterSnapshotId is not null && first.RosterSectionId is not null);
    }

    public async Task UpdateStatusAsync(string id, string status, string? endAt = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE delivery_sessions
            SET status = @Status,
                end_at = COALESCE(@EndAt, end_at)
            WHERE id = @Id;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Status = status, EndAt = endAt }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryCloseAsync(string id, string endAt, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE delivery_sessions SET status = 'closed', end_at = @EndAt WHERE id = @Id AND status IN ('active', 'paused');";
        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, EndAt = endAt }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<string>> GetInProgressAttemptIdsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var ids = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT id FROM student_attempts WHERE delivery_session_id = @SessionId AND status = 'in_progress' ORDER BY started_at;",
            new { SessionId = sessionId }, cancellationToken: cancellationToken));
        return ids.AsList();
    }

    public async Task<bool> DeleteIfNoAttemptsAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        var hasAttempts = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM student_attempts WHERE delivery_session_id = @Id);",
            new { Id = id }, transaction, cancellationToken: cancellationToken));
        if (hasAttempts)
        {
            transaction.Rollback();
            return false;
        }
        var deleted = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM delivery_sessions WHERE id = @Id;", new { Id = id }, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return deleted == 1;
    }

    private sealed class LocalDeliverySessionRow
    {
        public string Id { get; init; } = string.Empty;
        public string ExamVersionId { get; init; } = string.Empty;
        public string SchoolCode { get; init; } = string.Empty;
        public string? ClassroomCode { get; init; }
        public string? CommissionCode { get; init; }
        public string StartedBy { get; init; } = string.Empty;
        public string StartAt { get; init; } = string.Empty;
        public string? EndAt { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ConfigJson { get; init; }
        public string AccessCode { get; init; } = string.Empty;
        public long ExpectedStudentCount { get; init; }
        public string? SchoolYear { get; init; }
        public string? RosterSnapshotId { get; init; }
        public string? RosterSectionId { get; init; }

        public LocalDeliverySession ToDomain()
        {
            return new LocalDeliverySession(Id, ExamVersionId, SchoolCode, ClassroomCode, CommissionCode, StartedBy, StartAt, EndAt, Status, ConfigJson, AccessCode, checked((int)ExpectedStudentCount), SchoolYear, RosterSnapshotId, RosterSectionId);
        }
    }

    private static string FormatDisplayName(string? lastName, string? firstName)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return firstName ?? string.Empty;
        return string.IsNullOrWhiteSpace(firstName) ? lastName : $"{lastName}, {firstName}";
    }

    private static string? MaskDocument(string? last4) => string.IsNullOrWhiteSpace(last4) ? null : $"**.***.{last4}";

    private static (string? Grade, string? Division) ReadGradeFromMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return (null, null);
        try
        {
            using var metadata = JsonDocument.Parse(metadataJson);
            var root = metadata.RootElement;
            return (ReadString(root, "grade"), ReadString(root, "division"));
        }
        catch (JsonException) { return (null, null); }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class LocalSessionProgressRow
    {
        public string SessionId { get; init; } = string.Empty;
        public string AccessCode { get; init; } = string.Empty;
        public long ExpectedStudentCount { get; init; }
        public string? RosterSnapshotId { get; init; }
        public string? RosterSectionId { get; init; }
        public string? MetadataJson { get; init; }
        public string? Course { get; init; }
        public string? Division { get; init; }
        public string? Shift { get; init; }
        public string? Level { get; init; }
        public long StartedCount { get; init; }
        public long SubmittedCount { get; init; }
        public long InProgressCount { get; init; }
        public double? AverageScorePercent { get; init; }
        public string? StudentId { get; init; }
        public string? LastName { get; init; }
        public string? FirstName { get; init; }
        public string? DocumentLast4 { get; init; }
        public string? AttemptId { get; init; }
        public string? AttemptStatus { get; init; }
        public string? StartedAt { get; init; }
        public string? SubmittedAt { get; init; }
        public string? SubmissionReason { get; init; }
        public bool OffRoster { get; init; }
    }
}

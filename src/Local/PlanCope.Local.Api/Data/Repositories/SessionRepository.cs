using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class SessionRepository(ILocalSqliteConnectionFactory connectionFactory) : ISessionRepository
{
    public async Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count)
            VALUES (@Id, @ExamVersionId, @SchoolCode, @ClassroomCode, @CommissionCode, @StartedBy, @StartAt, @EndAt, @Status, @ConfigJson, @AccessCode, @ExpectedStudentCount);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, session, cancellationToken: cancellationToken));
    }

    public async Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count
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
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count
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
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count
            FROM delivery_sessions
            WHERE status IN ('active', 'paused')
            ORDER BY start_at DESC;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var sessions = await connection.QueryAsync<LocalDeliverySessionRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return sessions.Select(static row => row.ToDomain()).ToList();
    }

    public async Task<LocalSessionProgress?> GetProgressAsync(string idOrAccessCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                s.id AS session_id,
                s.access_code,
                s.expected_student_count,
                COUNT(a.id) AS started_count,
                COALESCE(SUM(CASE WHEN a.status = 'submitted' THEN 1 ELSE 0 END), 0) AS submitted_count,
                COALESCE(SUM(CASE WHEN a.status = 'in_progress' THEN 1 ELSE 0 END), 0) AS in_progress_count
            FROM delivery_sessions s
            LEFT JOIN student_attempts a ON a.delivery_session_id = s.id
            WHERE s.id = @IdOrAccessCode
               OR s.access_code = @AccessCode
            GROUP BY s.id, s.access_code, s.expected_student_count
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalSessionProgressRow>(new CommandDefinition(sql, new { IdOrAccessCode = idOrAccessCode, AccessCode = idOrAccessCode.ToUpperInvariant() }, cancellationToken: cancellationToken));
        return row?.ToDomain();
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

        public LocalDeliverySession ToDomain()
        {
            return new LocalDeliverySession(Id, ExamVersionId, SchoolCode, ClassroomCode, CommissionCode, StartedBy, StartAt, EndAt, Status, ConfigJson, AccessCode, checked((int)ExpectedStudentCount));
        }
    }

    private sealed class LocalSessionProgressRow
    {
        public string SessionId { get; init; } = string.Empty;
        public string AccessCode { get; init; } = string.Empty;
        public long ExpectedStudentCount { get; init; }
        public long StartedCount { get; init; }
        public long SubmittedCount { get; init; }
        public long InProgressCount { get; init; }

        public LocalSessionProgress ToDomain()
        {
            return new LocalSessionProgress(SessionId, AccessCode, checked((int)ExpectedStudentCount), checked((int)StartedCount), checked((int)SubmittedCount), checked((int)InProgressCount));
        }
    }
}

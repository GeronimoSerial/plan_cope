using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class SessionRepository(ILocalSqliteConnectionFactory connectionFactory) : ISessionRepository
{
    public async Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json)
            VALUES (@Id, @ExamVersionId, @SchoolCode, @ClassroomCode, @CommissionCode, @StartedBy, @StartAt, @EndAt, @Status, @ConfigJson);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, session, cancellationToken: cancellationToken));
    }

    public async Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json
            FROM delivery_sessions
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.QuerySingleOrDefaultAsync<LocalDeliverySession>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LocalDeliverySession>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json
            FROM delivery_sessions
            WHERE status IN ('active', 'paused')
            ORDER BY start_at DESC;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var sessions = await connection.QueryAsync<LocalDeliverySession>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return sessions.AsList();
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
}

using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class AttemptRepository(ILocalSqliteConnectionFactory connectionFactory) : IAttemptRepository
{
    public async Task CreateAsync(StudentAttempt attempt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code)
            VALUES (@Id, @DeliverySessionId, @StudentCode, @Status, @StartedAt, @SubmittedAt, @LocalSequence, @ConfirmationCode);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, attempt, cancellationToken: cancellationToken));
    }

    public async Task<StudentAttempt?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code
            FROM student_attempts
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<StudentAttemptRow>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<bool> ExistsForStudentAsync(string deliverySessionId, string studentCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM student_attempts
                WHERE delivery_session_id = @DeliverySessionId
                  AND student_code = @StudentCode
            );
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { DeliverySessionId = deliverySessionId, StudentCode = studentCode }, cancellationToken: cancellationToken));
    }

    public async Task<int> GetNextLocalSequenceAsync(string deliverySessionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COALESCE(MAX(local_sequence), 0) + 1
            FROM student_attempts
            WHERE delivery_session_id = @DeliverySessionId;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var next = await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { DeliverySessionId = deliverySessionId }, cancellationToken: cancellationToken));
        return checked((int)next);
    }

    public async Task UpsertAnswersAsync(string attemptId, IReadOnlyList<SubmissionAnswer> answers, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO submission_answers (id, student_attempt_id, block_id, answer_json, created_at)
            VALUES (@Id, @StudentAttemptId, @BlockId, @AnswerJson, @CreatedAt)
            ON CONFLICT (student_attempt_id, block_id)
            DO UPDATE SET answer_json = excluded.answer_json,
                          created_at = excluded.created_at;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var answer in answers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = answer with { StudentAttemptId = attemptId };
            await connection.ExecuteAsync(new CommandDefinition(sql, row, transaction, cancellationToken: cancellationToken));
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<SubmissionAnswer>> GetAnswersAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, student_attempt_id, block_id, answer_json, created_at
            FROM submission_answers
            WHERE student_attempt_id = @AttemptId
            ORDER BY created_at;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var answers = await connection.QueryAsync<SubmissionAnswer>(new CommandDefinition(sql, new { AttemptId = attemptId }, cancellationToken: cancellationToken));
        return answers.AsList();
    }

    public async Task SubmitAsync(string id, string submittedAt, string confirmationCode, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE student_attempts
            SET status = 'submitted',
                submitted_at = @SubmittedAt,
                confirmation_code = @ConfirmationCode
            WHERE id = @Id
              AND status = 'in_progress';
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, SubmittedAt = submittedAt, ConfirmationCode = confirmationCode }, cancellationToken: cancellationToken));
    }

    private sealed class StudentAttemptRow
    {
        public string Id { get; init; } = string.Empty;
        public string DeliverySessionId { get; init; } = string.Empty;
        public string StudentCode { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StartedAt { get; init; } = string.Empty;
        public string? SubmittedAt { get; init; }
        public long LocalSequence { get; init; }
        public string? ConfirmationCode { get; init; }

        public StudentAttempt ToDomain()
        {
            return new StudentAttempt(Id, DeliverySessionId, StudentCode, Status, StartedAt, SubmittedAt, checked((int)LocalSequence), ConfirmationCode);
        }
    }
}

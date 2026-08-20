using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class AttemptRepository(ILocalSqliteConnectionFactory connectionFactory) : IAttemptRepository
{
    public async Task CreateResolutionAsync(StudentResolution resolution, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO student_resolutions
                (id, delivery_session_id, roster_snapshot_id, roster_section_id, roster_student_id,
                 ge_person_id, first_name, last_name, document_last4, token_hash, expires_at, created_at)
            VALUES
                (@Id, @DeliverySessionId, @RosterSnapshotId, @RosterSectionId, @RosterStudentId,
                 @GePersonId, @FirstName, @LastName, @DocumentLast4, @TokenHash, @ExpiresAt, @CreatedAt);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, resolution, cancellationToken: cancellationToken));
    }

    public async Task<NominalAttemptStartResult> StartNominalAttemptAsync(
        string deliverySessionId,
        string tokenHash,
        string startedAt,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        var resolution = await connection.QuerySingleOrDefaultAsync<StudentResolutionRow>(new CommandDefinition(
            """
            SELECT id, delivery_session_id, roster_snapshot_id, roster_section_id, roster_student_id,
                   ge_person_id, first_name, last_name, document_last4, token_hash, expires_at, used_at
            FROM student_resolutions
            WHERE delivery_session_id = @DeliverySessionId AND token_hash = @TokenHash
            LIMIT 1;
            """,
            new { DeliverySessionId = deliverySessionId, TokenHash = tokenHash },
            transaction,
            cancellationToken: cancellationToken));

        if (resolution is null)
        {
            transaction.Rollback();
            return new(NominalAttemptStartStatus.ResolutionNotFound, null);
        }

        if (!string.IsNullOrWhiteSpace(resolution.UsedAt))
        {
            transaction.Rollback();
            return new(NominalAttemptStartStatus.ResolutionUsed, null);
        }

        if (!DateTimeOffset.TryParse(resolution.ExpiresAt, out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow)
        {
            transaction.Rollback();
            return new(NominalAttemptStartStatus.ResolutionExpired, null);
        }

        var existing = await connection.QuerySingleOrDefaultAsync<StudentAttemptRow>(new CommandDefinition(
            """
            SELECT id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence,
                   confirmation_code, roster_student_id, ge_person_id, student_first_name, student_last_name,
                   document_last4, verification_source, verified_at
            FROM student_attempts
            WHERE delivery_session_id = @DeliverySessionId AND ge_person_id = @GePersonId
            LIMIT 1;
            """,
            new { DeliverySessionId = deliverySessionId, resolution.GePersonId },
            transaction,
            cancellationToken: cancellationToken));
        if (existing is not null)
        {
            transaction.Rollback();
            return new(NominalAttemptStartStatus.AttemptExists, existing.ToDomain());
        }

        var usedAt = DateTimeOffset.UtcNow.ToString("O");
        var consumed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE student_resolutions
            SET used_at = @UsedAt
            WHERE id = @Id AND used_at IS NULL;
            """,
            new { resolution.Id, UsedAt = usedAt },
            transaction,
            cancellationToken: cancellationToken));
        if (consumed != 1)
        {
            transaction.Rollback();
            return new(NominalAttemptStartStatus.ResolutionUsed, null);
        }

        var nextSequence = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(MAX(local_sequence), 0) + 1
            FROM student_attempts
            WHERE delivery_session_id = @DeliverySessionId;
            """,
            new { DeliverySessionId = deliverySessionId },
            transaction,
            cancellationToken: cancellationToken));

        var attempt = new StudentAttempt(
            Guid.NewGuid().ToString(),
            deliverySessionId,
            $"GE:{resolution.GePersonId}",
            "in_progress",
            startedAt,
            null,
            checked((int)nextSequence),
            null,
            resolution.RosterStudentId,
            resolution.GePersonId,
            resolution.FirstName,
            resolution.LastName,
            resolution.DocumentLast4,
            "ge_roster",
            usedAt);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO student_attempts
                (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence,
                 confirmation_code, roster_student_id, ge_person_id, student_first_name, student_last_name,
                 document_last4, verification_source, verified_at)
            VALUES
                (@Id, @DeliverySessionId, @StudentCode, @Status, @StartedAt, @SubmittedAt, @LocalSequence,
                 @ConfirmationCode, @RosterStudentId, @GePersonId, @StudentFirstName, @StudentLastName,
                 @DocumentLast4, @VerificationSource, @VerifiedAt);
            """,
            attempt,
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return NominalAttemptStartResult.Started(attempt);
    }

    public async Task CreateAsync(StudentAttempt attempt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO student_attempts
                (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code,
                 roster_student_id, ge_person_id, student_first_name, student_last_name, document_last4, verification_source, verified_at)
            VALUES
                (@Id, @DeliverySessionId, @StudentCode, @Status, @StartedAt, @SubmittedAt, @LocalSequence, @ConfirmationCode,
                 @RosterStudentId, @GePersonId, @StudentFirstName, @StudentLastName, @DocumentLast4, @VerificationSource, @VerifiedAt);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, attempt, cancellationToken: cancellationToken));
    }

    public async Task<StudentAttempt?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code,
                   roster_student_id, ge_person_id, student_first_name, student_last_name, document_last4, verification_source, verified_at
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

    public async Task<bool> SubmitWithOutboxAsync(
        string id,
        string submittedAt,
        string confirmationCode,
        SyncOutbox outbox,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE student_attempts
            SET status = 'submitted',
                submitted_at = @SubmittedAt,
                confirmation_code = @ConfirmationCode
            WHERE id = @Id
              AND status = 'in_progress';
            """,
            new { Id = id, SubmittedAt = submittedAt, ConfirmationCode = confirmationCode },
            transaction,
            cancellationToken: cancellationToken));
        if (updated != 1)
        {
            transaction.Rollback();
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sync_outbox
                (id, event_type, aggregate_type, aggregate_id, idempotency_key, payload_json,
                 status, retry_count, next_retry_at, last_error, created_at, processed_at)
            VALUES
                (@Id, @EventType, @AggregateType, @AggregateId, @IdempotencyKey, @PayloadJson,
                 @Status, @RetryCount, @NextRetryAt, @LastError, @CreatedAt, @ProcessedAt);
            """,
            outbox,
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
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
        public string? RosterStudentId { get; init; }
        public int? GePersonId { get; init; }
        public string? StudentFirstName { get; init; }
        public string? StudentLastName { get; init; }
        public string? DocumentLast4 { get; init; }
        public string? VerificationSource { get; init; }
        public string? VerifiedAt { get; init; }

        public StudentAttempt ToDomain()
        {
            return new StudentAttempt(Id, DeliverySessionId, StudentCode, Status, StartedAt, SubmittedAt, checked((int)LocalSequence), ConfirmationCode,
                RosterStudentId, GePersonId, StudentFirstName, StudentLastName, DocumentLast4, VerificationSource, VerifiedAt);
        }
    }

    private sealed class StudentResolutionRow
    {
        public string Id { get; init; } = string.Empty;
        public string DeliverySessionId { get; init; } = string.Empty;
        public string RosterSnapshotId { get; init; } = string.Empty;
        public string RosterSectionId { get; init; } = string.Empty;
        public string RosterStudentId { get; init; } = string.Empty;
        public int GePersonId { get; init; }
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string DocumentLast4 { get; init; } = string.Empty;
        public string TokenHash { get; init; } = string.Empty;
        public string ExpiresAt { get; init; } = string.Empty;
        public string? UsedAt { get; init; }
    }
}

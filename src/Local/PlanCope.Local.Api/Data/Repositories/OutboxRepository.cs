using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class OutboxRepository(ILocalSqliteConnectionFactory connectionFactory) : IOutboxRepository
{
    public async Task InsertAsync(SyncOutbox item, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sync_outbox (id, event_type, aggregate_type, aggregate_id, idempotency_key, payload_json, status, retry_count, next_retry_at, last_error, created_at, processed_at)
            VALUES (@Id, @EventType, @AggregateType, @AggregateId, @IdempotencyKey, @PayloadJson, @Status, @RetryCount, @NextRetryAt, @LastError, @CreatedAt, @ProcessedAt);
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SyncOutbox>> GetPendingBatchAsync(int limit, string now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, event_type, aggregate_type, aggregate_id, idempotency_key, payload_json, status, retry_count, next_retry_at, last_error, created_at, processed_at
            FROM sync_outbox
            WHERE status = 'pending'
              AND (next_retry_at IS NULL OR next_retry_at <= @Now)
            ORDER BY created_at
            LIMIT @Limit;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<SyncOutboxRow>(new CommandDefinition(sql, new { Limit = limit, Now = now }, cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToDomain()).ToList();
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sync_outbox
            WHERE status = 'pending';
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task MarkSentAsync(string id, string processedAt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sync_outbox
            SET status = 'sent',
                processed_at = @ProcessedAt,
                last_error = NULL
            WHERE id = @Id;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, ProcessedAt = processedAt }, cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sync_outbox
            SET status = 'failed',
                last_error = @Error
            WHERE id = @Id;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Error = error }, cancellationToken: cancellationToken));
    }

    public async Task RequeueAsync(string id, int retryCount, string nextRetryAt, string? lastError, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sync_outbox
            SET status = 'pending',
                retry_count = @RetryCount,
                next_retry_at = @NextRetryAt,
                last_error = @LastError
            WHERE id = @Id;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, RetryCount = retryCount, NextRetryAt = nextRetryAt, LastError = lastError }, cancellationToken: cancellationToken));
    }

    private sealed class SyncOutboxRow
    {
        public string Id { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string AggregateType { get; init; } = string.Empty;
        public string AggregateId { get; init; } = string.Empty;
        public string IdempotencyKey { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public long RetryCount { get; init; }
        public string? NextRetryAt { get; init; }
        public string? LastError { get; init; }
        public string CreatedAt { get; init; } = string.Empty;
        public string? ProcessedAt { get; init; }

        public SyncOutbox ToDomain()
        {
            return new SyncOutbox(Id, EventType, AggregateType, AggregateId, IdempotencyKey, PayloadJson, Status, checked((int)RetryCount), NextRetryAt, LastError, CreatedAt, ProcessedAt);
        }
    }
}

using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class SyncStateRepository(ILocalSqliteConnectionFactory connectionFactory) : ISyncStateRepository
{
    public async Task<SyncState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, key, value_json, updated_at
            FROM sync_state
            WHERE key = @Key
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.QuerySingleOrDefaultAsync<SyncState>(new CommandDefinition(sql, new { Key = key }, cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sync_state (id, key, value_json, updated_at)
            VALUES (@Id, @Key, @ValueJson, @UpdatedAt)
            ON CONFLICT (key)
            DO UPDATE SET value_json = excluded.value_json,
                          updated_at = excluded.updated_at;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, state, cancellationToken: cancellationToken));
    }
}

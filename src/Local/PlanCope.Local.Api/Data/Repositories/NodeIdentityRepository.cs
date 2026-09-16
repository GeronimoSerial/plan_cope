using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class NodeIdentityRepository(ILocalSqliteConnectionFactory connectionFactory) : INodeIdentityRepository
{
    public async Task<NodeIdentity?> GetAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, node_id, cue, fingerprint_hash, fingerprint_components_json, enrolled_at, last_sync_at, credential_state, revocation_detected_at, revocation_stage
            FROM node_identity
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        return await connection.QuerySingleOrDefaultAsync<NodeIdentity>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO node_identity (id, node_id, cue, fingerprint_hash, fingerprint_components_json, enrolled_at, last_sync_at, credential_state, revocation_detected_at, revocation_stage)
            VALUES (@Id, @NodeId, @Cue, @FingerprintHash, @FingerprintComponentsJson, @EnrolledAt, @LastSyncAt, @CredentialState, @RevocationDetectedAt, @RevocationStage)
            ON CONFLICT (cue)
            DO UPDATE SET node_id = excluded.node_id,
                          fingerprint_hash = excluded.fingerprint_hash,
                          fingerprint_components_json = excluded.fingerprint_components_json,
                          enrolled_at = excluded.enrolled_at,
                          last_sync_at = excluded.last_sync_at,
                          credential_state = excluded.credential_state,
                          revocation_detected_at = excluded.revocation_detected_at,
                          revocation_stage = excluded.revocation_stage;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, identity, cancellationToken: cancellationToken));
    }
}
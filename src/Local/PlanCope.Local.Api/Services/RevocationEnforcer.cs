using System.Text.Json;
using Dapper;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

public enum RevocationEnforcementOutcome
{
    NotApplicable,
    AlreadyLocked,
    WaitingForSessionEnd,
    DrainIncomplete,
    Locked,
}

/// <summary>
/// Drives a revoked node through the fixed, ordered, resumable revocation sequence:
/// wait for active sessions to close, drain the outbox, wipe the roster cache and the
/// Central credential, then mark the node as locked. Each completed step is persisted to
/// <c>node_identity.revocation_stage</c> immediately so a crash resumes from the last
/// persisted stage instead of re-running destructive or already-accepted work.
/// </summary>
public sealed class RevocationEnforcer(
    INodeIdentityRepository nodeIdentityRepository,
    ISessionRepository sessionRepository,
    IOutboxRepository outboxRepository,
    ISyncStateRepository syncStateRepository,
    ILocalSqliteConnectionFactory connectionFactory,
    LocalOutboxPushService outboxPushService)
{
    private const string StageDrained = "drained";
    private const string StageWiped = "wiped";
    private const string StageLocked = "locked";

    private const int PushBatchLimit = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] CredentialKeys =
    [
        "central_access_token",
        "central_refresh_token",
        "central_access_token_expires_at",
        "central_refresh_token_expires_at",
        "node_id",
    ];

    public async Task<RevocationEnforcementOutcome> TryAdvanceAsync(CancellationToken cancellationToken = default)
    {
        var identity = await nodeIdentityRepository.GetAsync(cancellationToken);
        if (identity is null || identity.CredentialState != "revoked")
        {
            return RevocationEnforcementOutcome.NotApplicable;
        }

        if (identity.RevocationStage == StageLocked)
        {
            return RevocationEnforcementOutcome.AlreadyLocked;
        }

        var revocationDetectedAt = identity.RevocationDetectedAt ?? DateTimeOffset.UtcNow.ToString("O");

        var activeSessions = await sessionRepository.GetActiveAsync(cancellationToken);
        if (activeSessions.Count > 0)
        {
            return RevocationEnforcementOutcome.WaitingForSessionEnd;
        }

        var stage = identity.RevocationStage;
        if (stage != StageDrained && stage != StageWiped)
        {
            if (!await TryDrainAsync(cancellationToken))
            {
                return RevocationEnforcementOutcome.DrainIncomplete;
            }

            identity = identity with
            {
                RevocationStage = StageDrained,
                RevocationDetectedAt = revocationDetectedAt,
            };
            await nodeIdentityRepository.UpsertAsync(identity, cancellationToken);
            stage = StageDrained;
        }

        if (stage == StageDrained)
        {
            await WipeRosterCacheAsync(cancellationToken);
            await DestroyCentralCredentialAsync(cancellationToken);

            identity = identity with
            {
                RevocationStage = StageWiped,
                RevocationDetectedAt = revocationDetectedAt,
            };
            await nodeIdentityRepository.UpsertAsync(identity, cancellationToken);
        }

        identity = identity with
        {
            RevocationStage = StageLocked,
            RevocationDetectedAt = revocationDetectedAt,
        };
        await nodeIdentityRepository.UpsertAsync(identity, cancellationToken);

        return RevocationEnforcementOutcome.Locked;
    }

    private async Task<bool> TryDrainAsync(CancellationToken cancellationToken)
    {
        var noProgressTicks = 0;

        while (true)
        {
            if (await outboxRepository.CountPendingAsync(cancellationToken) == 0)
            {
                return true;
            }

            var result = await outboxPushService.PushAsync(PushBatchLimit, cancellationToken);
            if (result.Considered == 0 || (result.Accepted == 0 && result.Pending > 0))
            {
                noProgressTicks++;
                if (noProgressTicks >= 2)
                {
                    return false;
                }
            }
            else
            {
                noProgressTicks = 0;
            }
        }
    }

    private async Task WipeRosterCacheAsync(CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_students;", cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_sections;", cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_snapshots;", cancellationToken: cancellationToken));
    }

    private async Task DestroyCentralCredentialAsync(CancellationToken cancellationToken)
    {
        var emptied = JsonSerializer.Serialize(string.Empty, JsonOptions);
        var updatedAt = DateTimeOffset.UtcNow.ToString("O");

        foreach (var key in CredentialKeys)
        {
            await syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), key, emptied, updatedAt), cancellationToken);
        }
    }
}
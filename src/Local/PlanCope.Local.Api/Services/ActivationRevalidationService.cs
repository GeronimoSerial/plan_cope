using System.Text.Json;
using Dapper;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

public sealed class ActivationRevalidationService(
    INodeIdentityRepository identityRepository,
    ISyncStateRepository syncStateRepository,
    ILocalSqliteConnectionFactory connectionFactory,
    NodeCredentialRefresher credentialRefresher,
    AttemptSubmissionService attemptSubmissionService,
    LocalAssetFileService assetFileService,
    ILogger<ActivationRevalidationService> logger,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaximumWorkDeferral = TimeSpan.FromDays(7);
    private const int WarningDays = 5;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (await IsExpiredAsync(cancellationToken))
        {
            // The SQLite wipe commits before filesystem cleanup. Repeat this idempotent step
            // after a crash, including after the identity row has already been removed.
            await CompletePendingAssetCleanupAsync(cancellationToken);
            return;
        }

        var identity = await identityRepository.GetAsync(cancellationToken);
        if (identity is null || identity.CredentialState != "active") return;

        var lastServerTime = await ReadDateAsync("last_server_time", cancellationToken);
        var lastSuccessful = await ReadDateAsync("last_revalidation_at", cancellationToken)
            ?? ParseDate(identity.EnrolledAt)
            ?? DateTimeOffset.UtcNow;
        var intervalDays = await ReadIntAsync("revalidation_interval_days", cancellationToken) ?? 30;
        intervalDays = Math.Clamp(intervalDays, 1, 365);

        // Anchor elapsed time to the paired local/server timestamps recorded at successful
        // contact. This prevents a pre-existing future local clock from expiring immediately.
        var now = await GetTrustedNowAsync(lastServerTime ?? lastSuccessful, cancellationToken);
        var deadline = lastSuccessful.AddDays(intervalDays);
        var dueAt = deadline.AddDays(-WarningDays);

        if (now >= dueAt)
        {
            var lastAttempt = await ReadDateAsync("last_revalidation_attempt_at", cancellationToken);
            if (lastAttempt is null || now < lastAttempt || now - lastAttempt >= RetryInterval)
            {
                await WriteStringAsync("last_revalidation_attempt_at", now.ToUniversalTime().ToString("O"), cancellationToken);
                if (await credentialRefresher.TryRefreshAsync(cancellationToken))
                {
                    await SetExpiryPendingAsync(false, cancellationToken);
                    return;
                }
            }
        }

        // Re-read because a refresh attempt can discover revocation and update Local identity.
        identity = await identityRepository.GetAsync(cancellationToken);
        if (identity?.CredentialState == "revoked" || identity is null) return;

        lastServerTime = await ReadDateAsync("last_server_time", cancellationToken);
        now = await GetTrustedNowAsync(lastServerTime ?? lastSuccessful, cancellationToken);
        lastSuccessful = await ReadDateAsync("last_revalidation_at", cancellationToken)
            ?? ParseDate(identity.EnrolledAt)
            ?? DateTimeOffset.UtcNow;
        intervalDays = Math.Clamp(await ReadIntAsync("revalidation_interval_days", cancellationToken) ?? 30, 1, 365);
        if (now >= lastSuccessful.AddDays(intervalDays))
        {
            await WriteBoolAsync("activation_expiry_pending", true, cancellationToken);
            await ExpireAndWipeAsync(cancellationToken);
        }
    }

    public async Task<int?> GetDaysRemainingAsync(CancellationToken cancellationToken = default)
    {
        var identity = await identityRepository.GetAsync(cancellationToken);
        if (identity is null || identity.CredentialState != "active") return null;
        var lastSuccessful = await ReadDateAsync("last_revalidation_at", cancellationToken)
            ?? ParseDate(identity.EnrolledAt)
            ?? DateTimeOffset.UtcNow;
        var interval = Math.Clamp(await ReadIntAsync("revalidation_interval_days", cancellationToken) ?? 30, 1, 365);
        var now = await GetTrustedNowAsync(await ReadDateAsync("last_server_time", cancellationToken) ?? lastSuccessful, cancellationToken);
        return Math.Max(0, (int)Math.Ceiling((lastSuccessful.AddDays(interval) - now).TotalDays));
    }

    public async Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default) =>
        await ReadBoolAsync("activation_expired", cancellationToken);

    public async Task<bool> IsActivationInProgressAsync(CancellationToken cancellationToken = default) =>
        await ReadBoolAsync("activation_in_progress", cancellationToken);

    public async Task<bool> IsExpiryPendingAsync(CancellationToken cancellationToken = default) =>
        await ReadBoolAsync("activation_expiry_pending", cancellationToken);

    public async Task SetExpiryPendingAsync(bool pending, CancellationToken cancellationToken = default)
    {
        await WriteBoolAsync("activation_expiry_pending", pending, cancellationToken);
        if (!pending)
        {
            using var connection = connectionFactory.CreateOpenConnection();
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM sync_state WHERE key='activation_expiry_pending_since';", cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> IsLocalClockWarningAsync(CancellationToken cancellationToken = default)
    {
        var contactServer = await ReadDateAsync("last_server_contact_time", cancellationToken);
        var contactLocal = await ReadDateAsync("last_server_contact_local_time", cancellationToken);
        if (contactServer is null || contactLocal is null) return false;
        var elapsed = timeProvider.GetUtcNow() - contactLocal.Value;
        var expectedLocal = contactServer.Value + (elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero);
        return timeProvider.GetUtcNow() - expectedLocal > TimeSpan.FromHours(24);
    }

    private async Task<DateTimeOffset> GetTrustedNowAsync(DateTimeOffset fallback, CancellationToken cancellationToken)
    {
        var contactServer = await ReadDateAsync("last_server_contact_time", cancellationToken);
        var contactLocal = await ReadDateAsync("last_server_contact_local_time", cancellationToken);
        if (contactServer is null || contactLocal is null) return Max(timeProvider.GetUtcNow(), fallback);
        var localNow = timeProvider.GetUtcNow();
        // If the operator corrects a future clock back to Central time, establish a fresh local
        // anchor so elapsed trusted time can advance again instead of waiting for the old date.
        if (localNow < contactLocal.Value && localNow >= contactServer.Value.AddHours(-24))
        {
            await WriteStringAsync("last_server_contact_local_time", localNow.ToString("O"), cancellationToken);
            contactLocal = localNow;
        }
        var elapsed = localNow - contactLocal.Value;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return Max(fallback, contactServer.Value + elapsed);
    }

    public Task SetActivationInProgressAsync(bool inProgress, CancellationToken cancellationToken = default) =>
        WriteBoolAsync("activation_in_progress", inProgress, cancellationToken);

    public Task ClearExpiredFlagAsync(CancellationToken cancellationToken = default) =>
        WriteBoolAsync("activation_expired", false, cancellationToken);

    public async Task CompletePendingAssetCleanupAsync(CancellationToken cancellationToken = default)
    {
        if (!await ReadBoolAsync("activation_assets_cleanup_pending", cancellationToken)) return;
        assetFileService.ClearAll();
        await WriteBoolAsync("activation_assets_cleanup_pending", false, cancellationToken);
    }

    private async Task ExpireAndWipeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = connectionFactory.CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            var hasActiveWork = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM delivery_sessions WHERE lower(status) IN ('active', 'paused'))
                    OR EXISTS (SELECT 1 FROM student_attempts WHERE submitted_at IS NULL);
                """, transaction: transaction, cancellationToken: cancellationToken));
            var pendingSinceJson = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT value_json FROM sync_state WHERE key='activation_expiry_pending_since';", transaction: transaction, cancellationToken: cancellationToken));
            var pendingSince = DateTimeOffset.TryParse(pendingSinceJson?.Trim('"'), out var parsedPending) ? parsedPending : timeProvider.GetUtcNow();
            if (hasActiveWork && timeProvider.GetUtcNow() - pendingSince < MaximumWorkDeferral)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO sync_state (id,key,value_json,updated_at) VALUES (@Id,'activation_expiry_pending_since',@Value,@Now) " +
                    "ON CONFLICT(key) DO UPDATE SET value_json=excluded.value_json,updated_at=excluded.updated_at;",
                    new { Id = Guid.NewGuid().ToString("N"), Value = JsonSerializer.Serialize(pendingSince, JsonOptions), Now = DateTimeOffset.UtcNow.ToString("O") },
                    transaction, cancellationToken: cancellationToken));
                transaction.Commit();
                return;
            }
            if (hasActiveWork)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE delivery_sessions SET status='closed' WHERE lower(status) IN ('active','paused');",
                    transaction: transaction, cancellationToken: cancellationToken));
                transaction.Commit();
                await attemptSubmissionService.FinalizeUnsubmittedAttemptsAsync(cancellationToken);
                await ExpireAndWipeAsync(cancellationToken);
                return;
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES (@Id, 'activation_expired', @Value, @Now) " +
                "ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;",
                new { Id = Guid.NewGuid().ToString("N"), Value = JsonSerializer.Serialize(true, JsonOptions), Now = DateTimeOffset.UtcNow.ToString("O") },
                transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES (@Id, 'activation_assets_cleanup_pending', @Value, @Now) " +
                "ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;",
                new { Id = Guid.NewGuid().ToString("N"), Value = JsonSerializer.Serialize(true, JsonOptions), Now = DateTimeOffset.UtcNow.ToString("O") },
                transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM student_resolutions;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM stats_rollup_blocks;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM stats_rollups;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM delivery_sessions;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_students;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_sections;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_snapshots;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM schools;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_exam_versions;", transaction: transaction, cancellationToken: cancellationToken));
            foreach (var key in new[]
            {
                "node_id", "central_access_token", "central_refresh_token", "central_access_token_expires_at",
                "central_refresh_token_expires_at", "last_revalidation_at", "last_server_time", "last_server_contact_time", "last_server_contact_local_time", "last_effective_time",
                "last_revalidation_attempt_at", "revalidation_interval_days", "last_exam_pull_cursor", "activation_in_progress",
                "activation_expiry_pending", "activation_expiry_pending_since", "central_url"
            })
                await connection.ExecuteAsync(new CommandDefinition("DELETE FROM sync_state WHERE key = @Key;", new { Key = key }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM node_identity;", transaction: transaction, cancellationToken: cancellationToken));
            transaction.Commit();
            assetFileService.ClearAll();
            await WriteBoolAsync("activation_assets_cleanup_pending", false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Activation expiry data wipe failed; access remains blocked and the wipe will retry.");
            throw;
        }
    }

    private async Task<DateTimeOffset?> ReadDateAsync(string key, CancellationToken ct)
    {
        var value = await ReadStringAsync(key, ct);
        if (DateTimeOffset.TryParse(value, out var date)) return date.ToUniversalTime();
        return null;
    }

    private async Task<int?> ReadIntAsync(string key, CancellationToken ct)
    {
        var value = await ReadStringAsync(key, ct);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<bool> ReadBoolAsync(string key, CancellationToken ct) =>
        bool.TryParse(await ReadStringAsync(key, ct), out var value) && value;

    private async Task<string?> ReadStringAsync(string key, CancellationToken ct)
    {
        var state = await syncStateRepository.GetAsync(key, ct);
        if (string.IsNullOrWhiteSpace(state?.ValueJson)) return null;
        try
        {
            using var json = JsonDocument.Parse(state.ValueJson);
            return json.RootElement.ValueKind == JsonValueKind.String ? json.RootElement.GetString() : json.RootElement.GetRawText();
        }
        catch (JsonException) { return state.ValueJson; }
    }

    private Task WriteBoolAsync(string key, bool value, CancellationToken ct) =>
        WriteStringAsync(key, value.ToString(), ct);

    private Task WriteStringAsync(string key, string value, CancellationToken ct) =>
        syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), key,
            JsonSerializer.Serialize(value, JsonOptions), DateTimeOffset.UtcNow.ToString("O")), ct);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.ToUniversalTime() : null;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;
}

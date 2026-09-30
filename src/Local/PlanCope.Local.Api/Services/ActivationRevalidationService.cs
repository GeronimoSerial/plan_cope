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
    LocalAssetFileService assetFileService,
    ILogger<ActivationRevalidationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(6);
    private const int WarningDays = 5;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var identity = await identityRepository.GetAsync(cancellationToken);
        if (identity is null || identity.CredentialState != "active") return;

        var lastServerTime = await ReadDateAsync("last_server_time", cancellationToken);
        var lastSuccessful = await ReadDateAsync("last_revalidation_at", cancellationToken)
            ?? ParseDate(identity.EnrolledAt)
            ?? DateTimeOffset.UtcNow;
        var intervalDays = await ReadIntAsync("revalidation_interval_days", cancellationToken) ?? 30;
        intervalDays = Math.Clamp(intervalDays, 1, 365);

        // Keep a persisted high-water mark so a backwards wall-clock change cannot make the
        // trusted date move backwards across restarts. Server time is the initial anchor.
        var now = Max(DateTimeOffset.UtcNow, lastServerTime, await ReadDateAsync("last_effective_time", cancellationToken));
        await WriteDateAsync("last_effective_time", now, cancellationToken);
        var deadline = lastSuccessful.AddDays(intervalDays);
        var dueAt = deadline.AddDays(-WarningDays);

        if (now >= dueAt)
        {
            var lastAttempt = await ReadDateAsync("last_revalidation_attempt_at", cancellationToken);
            if (now >= deadline || lastAttempt is null || now - lastAttempt >= RetryInterval)
            {
                await WriteDateAsync("last_revalidation_attempt_at", now, cancellationToken);
                if (await credentialRefresher.TryRefreshAsync(cancellationToken)) return;
            }
        }

        // Re-read because a refresh attempt can discover revocation and update Local identity.
        identity = await identityRepository.GetAsync(cancellationToken);
        if (identity?.CredentialState == "revoked" || identity is null) return;

        lastServerTime = await ReadDateAsync("last_server_time", cancellationToken);
        now = Max(DateTimeOffset.UtcNow, lastServerTime, await ReadDateAsync("last_effective_time", cancellationToken));
        await WriteDateAsync("last_effective_time", now, cancellationToken);
        lastSuccessful = await ReadDateAsync("last_revalidation_at", cancellationToken)
            ?? ParseDate(identity.EnrolledAt)
            ?? DateTimeOffset.UtcNow;
        intervalDays = Math.Clamp(await ReadIntAsync("revalidation_interval_days", cancellationToken) ?? 30, 1, 365);
        if (now >= lastSuccessful.AddDays(intervalDays))
        {
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
        var now = Max(DateTimeOffset.UtcNow, await ReadDateAsync("last_server_time", cancellationToken),
            await ReadDateAsync("last_effective_time", cancellationToken));
        return Math.Max(0, (int)Math.Ceiling((lastSuccessful.AddDays(interval) - now).TotalDays));
    }

    public async Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default) =>
        await ReadBoolAsync("activation_expired", cancellationToken);

    public async Task<bool> IsActivationInProgressAsync(CancellationToken cancellationToken = default) =>
        await ReadBoolAsync("activation_in_progress", cancellationToken);

    public Task SetActivationInProgressAsync(bool inProgress, CancellationToken cancellationToken = default) =>
        WriteBoolAsync("activation_in_progress", inProgress, cancellationToken);

    public Task ClearExpiredFlagAsync(CancellationToken cancellationToken = default) =>
        WriteBoolAsync("activation_expired", false, cancellationToken);

    private async Task ExpireAndWipeAsync(CancellationToken cancellationToken)
    {
        await WriteBoolAsync("activation_expired", true, cancellationToken);
        try
        {
            using var connection = connectionFactory.CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM delivery_sessions;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM student_resolutions;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_students;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_sections;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_roster_snapshots;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM schools;", transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM local_exam_versions;", transaction: transaction, cancellationToken: cancellationToken));
            foreach (var key in new[]
            {
                "node_id", "central_access_token", "central_refresh_token", "central_access_token_expires_at",
                "central_refresh_token_expires_at", "last_revalidation_at", "last_server_time", "last_effective_time",
                "last_revalidation_attempt_at", "revalidation_interval_days", "last_exam_pull_cursor"
            })
                await connection.ExecuteAsync(new CommandDefinition("DELETE FROM sync_state WHERE key = @Key;", new { Key = key }, transaction, cancellationToken: cancellationToken));
            transaction.Commit();
            assetFileService.ClearAll();
            await identityRepository.DeleteAsync(cancellationToken);
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

    private Task WriteDateAsync(string key, DateTimeOffset date, CancellationToken ct) =>
        WriteStringAsync(key, date.ToUniversalTime().ToString("O"), ct);

    private Task WriteBoolAsync(string key, bool value, CancellationToken ct) =>
        WriteStringAsync(key, value.ToString(), ct);

    private Task WriteStringAsync(string key, string value, CancellationToken ct) =>
        syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), key,
            JsonSerializer.Serialize(value, JsonOptions), DateTimeOffset.UtcNow.ToString("O")), ct);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset? b, DateTimeOffset? c) =>
        new[] { a, b ?? DateTimeOffset.MinValue, c ?? DateTimeOffset.MinValue }.Max();

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.ToUniversalTime() : null;
}

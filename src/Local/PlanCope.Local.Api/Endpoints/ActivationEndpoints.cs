using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using System.Text.Json;

namespace PlanCope.Local.Api.Endpoints;

public static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/activation/status", async (INodeIdentityRepository repository, ISyncStateRepository syncState,
            ActivationRevalidationService revalidation, CancellationToken ct) =>
        {
            var identity = await repository.GetAsync(ct);
            var expired = await revalidation.IsExpiredAsync(ct);
            var inProgress = await revalidation.IsActivationInProgressAsync(ct);
            var daysRemaining = await revalidation.GetDaysRemainingAsync(ct);
            ActivationDownloadProgress? downloadProgress = null;
            var progressState = await syncState.GetAsync("activation_download_progress", ct);
            if (!string.IsNullOrWhiteSpace(progressState?.ValueJson))
            {
                try { downloadProgress = JsonSerializer.Deserialize<ActivationDownloadProgress>(progressState.ValueJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
                catch (JsonException) { /* A stale or malformed progress marker must not block activation. */ }
            }
            return Results.Ok(new
            {
                phaseAComplete = identity?.CredentialState == "active" && !expired && !inProgress,
                cue = identity?.Cue,
                isLocked = identity?.RevocationStage == "locked" || expired,
                activationInProgress = inProgress,
                expiryPending = await revalidation.IsExpiryPendingAsync(ct),
                localClockWarning = await revalidation.IsLocalClockWarningAsync(ct),
                revalidationDaysRemaining = daysRemaining,
                revalidationWarning = daysRemaining is <= 5,
                downloadProgress
            });
        });
        return endpoints;
    }
}

using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;

namespace PlanCope.Local.Api.Endpoints;

public static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/activation/status", async (INodeIdentityRepository repository, ActivationRevalidationService revalidation, CancellationToken ct) =>
        {
            var identity = await repository.GetAsync(ct);
            var expired = await revalidation.IsExpiredAsync(ct);
            var inProgress = await revalidation.IsActivationInProgressAsync(ct);
            var daysRemaining = await revalidation.GetDaysRemainingAsync(ct);
            return Results.Ok(new
            {
                phaseAComplete = identity?.CredentialState == "active" && !expired && !inProgress,
                cue = identity?.Cue,
                isLocked = identity?.RevocationStage == "locked" || expired || inProgress,
                activationInProgress = inProgress,
                expiryPending = await revalidation.IsExpiryPendingAsync(ct),
                revalidationDaysRemaining = daysRemaining,
                revalidationWarning = daysRemaining is <= 5
            });
        });
        return endpoints;
    }
}

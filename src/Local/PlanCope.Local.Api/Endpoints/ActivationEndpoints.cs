using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Endpoints;

public static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/activation/status", async (INodeIdentityRepository repository, CancellationToken ct) =>
        {
            var identity = await repository.GetAsync(ct);
            return Results.Ok(new
            {
                phaseAComplete = identity?.CredentialState == "active",
                cue = identity?.Cue,
                isLocked = identity?.RevocationStage == "locked"
            });
        });
        return endpoints;
    }
}

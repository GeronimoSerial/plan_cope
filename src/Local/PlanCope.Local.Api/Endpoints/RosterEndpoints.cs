using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Local.Api.Endpoints;

public static class RosterEndpoints
{
    public static IEndpointRouteBuilder MapRosterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/rosters");

        group.MapGet("/latest", async (
            string? cue,
            string? schoolYear,
            ILocalRosterRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!CueCode.TryNormalize(cue, out var normalizedCue))
            {
                return Results.BadRequest(new { error = $"cue must contain exactly {CueCode.Length} digits." });
            }

            var snapshot = string.IsNullOrWhiteSpace(schoolYear)
                ? await repository.GetLatestSnapshotAsync(normalizedCue, cancellationToken)
                : await repository.GetLatestSnapshotAsync(normalizedCue, schoolYear, cancellationToken);
            var sections = snapshot is null
                ? []
                : await repository.GetSectionsAsync(normalizedCue, snapshot.SchoolYear, cancellationToken);
            return Results.Ok(new { snapshot, sections });
        });

        group.MapGet("/sections", async (
            string? cue,
            string? schoolYear,
            ILocalRosterRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!CueCode.TryNormalize(cue, out var normalizedCue) || string.IsNullOrWhiteSpace(schoolYear))
            {
                return Results.BadRequest(new { error = $"cue must contain exactly {CueCode.Length} digits and schoolYear is required." });
            }

            return Results.Ok(await repository.GetSectionsAsync(normalizedCue, schoolYear, cancellationToken));
        });

        return endpoints;
    }
}

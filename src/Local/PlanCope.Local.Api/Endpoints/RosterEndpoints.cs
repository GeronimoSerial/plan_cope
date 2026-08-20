using PlanCope.Local.Api.Data.Repositories;

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
            if (string.IsNullOrWhiteSpace(cue) || string.IsNullOrWhiteSpace(schoolYear))
            {
                return Results.BadRequest(new { error = "cue and schoolYear are required." });
            }

            var snapshot = await repository.GetLatestSnapshotAsync(cue, schoolYear, cancellationToken);
            var sections = await repository.GetSectionsAsync(cue, schoolYear, cancellationToken);
            return Results.Ok(new { snapshot, sections });
        });

        group.MapGet("/sections", async (
            string? cue,
            string? schoolYear,
            ILocalRosterRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue) || string.IsNullOrWhiteSpace(schoolYear))
            {
                return Results.BadRequest(new { error = "cue and schoolYear are required." });
            }

            return Results.Ok(await repository.GetSectionsAsync(cue, schoolYear, cancellationToken));
        });

        return endpoints;
    }
}

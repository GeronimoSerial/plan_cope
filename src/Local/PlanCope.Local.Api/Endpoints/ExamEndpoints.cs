using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Endpoints;

public static class ExamEndpoints
{
    public static IEndpointRouteBuilder MapExamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/exams");

        group.MapGet("/", async (string? grade, ILocalExamRepository repository, CancellationToken cancellationToken) =>
        {
            var exams = await repository.GetExamsAsync(grade, cancellationToken);
            return Results.Ok(exams);
        });

        group.MapGet("/{id}/blocks", async (string id, ILocalExamRepository repository, CancellationToken cancellationToken) =>
        {
            var exam = await repository.GetByIdAsync(id, cancellationToken);

            if (exam is null)
            {
                return Results.NotFound();
            }

            var blocks = await repository.GetBlocksAsync(id, cancellationToken);
            return Results.Ok(blocks);
        });

        return endpoints;
    }
}

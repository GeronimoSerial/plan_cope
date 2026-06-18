using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;

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

        group.MapPost("/import", async (
            ImportLocalExamRequest request,
            LocalExamImportService importer,
            CancellationToken cancellationToken) =>
        {
            var result = await importer.ImportAsync(request, cancellationToken);
            return result.IsValid ? Results.Ok(result.Response) : Results.BadRequest(new { errors = result.Errors });
        });

        return endpoints;
    }

    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/assets/{id}", async (
            string id,
            LocalAssetFileService assetFileService,
            CancellationToken cancellationToken) =>
        {
            var asset = await assetFileService.ResolveAsync(id, cancellationToken);
            return asset is null ? Results.NotFound() : Results.File(asset.Path, asset.MimeType);
        });

        return endpoints;
    }
}

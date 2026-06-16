using FluentValidation;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sessions");

        group.MapGet("/active", async (ISessionRepository repository, CancellationToken cancellationToken) =>
        {
            var sessions = await repository.GetActiveAsync(cancellationToken);
            return Results.Ok(sessions);
        });

        group.MapPost("/", async (
            CreateSessionRequest request,
            IValidator<CreateSessionRequest> validator,
            ISessionRepository sessionRepository,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);

            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var session = new LocalDeliverySession(
                Guid.NewGuid().ToString(),
                request.ExamVersionId,
                request.SchoolCode,
                request.ClassroomCode,
                request.CommissionCode,
                request.StartedBy,
                DateTimeOffset.UtcNow.ToString("O"),
                null,
                "active",
                request.Config?.GetRawText());

            await sessionRepository.CreateAsync(session, cancellationToken);
            return Results.Created($"/api/sessions/{session.Id}", session);
        });

        group.MapPut("/{id}/status", async (
            string id,
            UpdateSessionStatusRequest request,
            ISessionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var session = await repository.GetByIdAsync(id, cancellationToken);

            if (session is null)
            {
                return Results.NotFound();
            }

            if (session.Status is "closed")
            {
                return Results.BadRequest(new { error = "Closed sessions cannot transition." });
            }

            if (!IsAllowedTransition(session.Status, request.Status))
            {
                return Results.BadRequest(new { error = $"Invalid session transition from {session.Status} to {request.Status}." });
            }

            var endAt = request.Status is "closed" ? DateTimeOffset.UtcNow.ToString("O") : null;
            await repository.UpdateStatusAsync(id, request.Status, endAt, cancellationToken);

            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool IsAllowedTransition(string current, string next)
    {
        return (current, next) is
            ("active", "paused") or
            ("paused", "active") or
            ("active", "closed");
    }
}

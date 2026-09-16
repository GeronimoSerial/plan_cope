using System.Security.Cryptography;
using FluentValidation;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Domain.ValueObjects;

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

        group.MapGet("/{idOrAccessCode}", async (
            string idOrAccessCode,
            ISessionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var session = await repository.GetByIdOrAccessCodeAsync(idOrAccessCode, cancellationToken);
            return session is null ? Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." }) : Results.Ok(session);
        });

        group.MapPost("/", async (
            CreateSessionRequest request,
            IValidator<CreateSessionRequest> validator,
            ISessionRepository sessionRepository,
            ILocalRosterRepository rosterRepository,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);

            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            request = request with { SchoolCode = CueCode.Normalize(request.SchoolCode) };

            if (request.RosterSnapshotId is not null)
            {
                var rosterValidation = await rosterRepository.ValidateSelectionAsync(
                    request.SchoolCode,
                    request.SchoolYear!,
                    request.RosterSnapshotId,
                    request.RosterSectionId!,
                    cancellationToken);
                if (!rosterValidation.IsValid)
                {
                    return Results.BadRequest(new { error = rosterValidation.Error });
                }

                request = request with { ExpectedStudentCount = rosterValidation.StudentCount!.Value };
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
                request.Config?.GetRawText(),
                await GenerateAccessCodeAsync(sessionRepository, cancellationToken),
                request.ExpectedStudentCount,
                request.SchoolYear,
                request.RosterSnapshotId,
                request.RosterSectionId);

            await sessionRepository.CreateAsync(session, cancellationToken);
            return Results.Created($"/api/sessions/{session.Id}", session);
        });

        group.MapGet("/{idOrAccessCode}/progress", async (
            string idOrAccessCode,
            ISessionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var progress = await repository.GetProgressAsync(idOrAccessCode, cancellationToken);
            return progress is null ? Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." }) : Results.Ok(progress);
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
                return Results.BadRequest(new { error = "Esta sesión ya está cerrada y no admite más cambios de estado." });
            }

            if (!IsAllowedTransition(session.Status, request.Status))
            {
                return Results.BadRequest(new { error = $"No se puede pasar la sesión de \"{session.Status}\" a \"{request.Status}\"." });
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

    private static async Task<string> GenerateAccessCodeAsync(ISessionRepository repository, CancellationToken cancellationToken)
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = new char[5];
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var accessCode = new string(code);
            if (!await repository.AccessCodeExistsAsync(accessCode, cancellationToken))
            {
                return accessCode;
            }
        }

        throw new InvalidOperationException("Could not generate a unique session access code.");
    }
}

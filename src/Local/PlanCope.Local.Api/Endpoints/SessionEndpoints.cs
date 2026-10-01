using System.Security.Cryptography;
using FluentValidation;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
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

        group.MapGet("/active", async (string? schoolCode, ISessionRepository repository, CancellationToken cancellationToken) =>
        {
            var sessions = await repository.GetActiveSummariesAsync(schoolCode, cancellationToken);
            return Results.Ok(sessions);
        });

        group.MapGet("/history", async (string? schoolCode, string? status, string? course, string? division, string? shift, string? q, int? page, int? pageSize,
            ISessionRepository repository, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(status) && status.Trim().ToLowerInvariant() is not ("active" or "paused" or "closed" or "abierta" or "pausada" or "cerrada"))
                return Results.BadRequest(new { error = "El estado de sesión no es válido." });
            var currentPage = Math.Max(1, page ?? 1);
            var currentPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
            return Results.Ok(await repository.GetHistoryAsync(schoolCode, status, course, division, shift, q, currentPage, currentPageSize, cancellationToken));
        });

        group.MapGet("/history/filters", async (ISessionRepository repository, CancellationToken cancellationToken) =>
            Results.Ok(await repository.GetHistoryGradeSectionsAsync(cancellationToken)));

        endpoints.MapGet("/api/schools", async (bool? withAttempts, ISessionRepository repository, CancellationToken cancellationToken) =>
            withAttempts == true
                ? Results.Ok(await repository.GetSchoolsWithAttemptsAsync(cancellationToken))
                : Results.Ok(await repository.GetSchoolsAsync(cancellationToken)));

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
            ActivationRevalidationService activationRevalidation,
            CancellationToken cancellationToken) =>
        {
            if (await activationRevalidation.IsExpiryPendingAsync(cancellationToken))
            {
                return Results.Json(new { error = "El equipo debe revalidarse. Finalizá y enviá la evaluación en curso antes de volver a conectarte." }, statusCode: StatusCodes.Status423Locked);
            }

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

        group.MapPost("/{id}/extra-students", async (
            string id,
            AddSessionExtraStudentRequest request,
            ISessionRepository sessionRepository,
            ILocalRosterRepository rosterRepository,
            IAttemptRepository attemptRepository,
            IDocumentHmacService documentHmacService,
            CancellationToken cancellationToken) =>
        {
            var session = await sessionRepository.GetByIdAsync(id, cancellationToken);
            if (session is null) return Results.NotFound(new { error = "No encontramos esa sesión." });
            if (session.Status is not ("active" or "paused"))
                return Results.BadRequest(new { error = "Solo se pueden agregar estudiantes a una sesión abierta o pausada." });
            if (session.RosterSnapshotId is null || session.RosterSectionId is null)
                return Results.BadRequest(new { error = "Esta opción está disponible solo para sesiones nominales." });
            if (string.IsNullOrWhiteSpace(request.Document)) return Results.BadRequest(new { error = "El DNI es obligatorio." });
            var firstName = request.FirstName?.Trim();
            var lastName = request.LastName?.Trim();
            if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
                return Results.BadRequest(new { error = "El nombre y el apellido son obligatorios." });
            if (firstName.Length > 256 || lastName.Length > 256)
                return Results.BadRequest(new { error = "El nombre y el apellido no pueden superar los 256 caracteres." });

            string documentHash;
            string documentLast4;
            try
            {
                documentHash = documentHmacService.ComputeHash(request.Document);
                documentLast4 = documentHmacService.ComputeLast4(request.Document);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "El DNI no es válido." });
            }

            if (await rosterRepository.FindStudentAsync(session.RosterSnapshotId, session.RosterSectionId,
                    request.Document, documentHmacService, cancellationToken) is not null)
                return Results.Conflict(new { error = "Ese DNI ya figura en el padrón de la sección." });

            var student = new SessionExtraStudent(Guid.NewGuid().ToString(), session.Id, documentHash, documentLast4,
                firstName, lastName, DateTimeOffset.UtcNow.ToString("O"));
            if (!await attemptRepository.AddExtraStudentAsync(student, cancellationToken))
                return Results.Conflict(new { error = "Ese DNI ya fue agregado a esta sesión." });
            return Results.Created($"/api/sessions/{session.Id}/extra-students/{student.Id}", new
            {
                student.Id,
                student.FirstName,
                student.LastName,
                maskedDocument = $"**.***.{student.DocumentLast4}",
                offRoster = true
            });
        });

        group.MapDelete("/{id}/extra-students/{studentId}", async (
            string id,
            string studentId,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            CancellationToken cancellationToken) =>
        {
            var session = await sessionRepository.GetByIdAsync(id, cancellationToken);
            if (session is null) return Results.NotFound(new { error = "No encontramos esa sesión." });
            if (session.Status is not ("active" or "paused"))
                return Results.BadRequest(new { error = "Solo se pueden quitar estudiantes de una sesión abierta o pausada." });
            if (!await attemptRepository.RemoveExtraStudentAsync(id, studentId, cancellationToken))
                return Results.Conflict(new { error = "No se puede quitar: el estudiante ya empezó su evaluación o no pertenece a esta sesión." });
            return Results.NoContent();
        });

        group.MapPut("/{id}/status", async (
            string id,
            UpdateSessionStatusRequest request,
            ISessionRepository repository,
            IAttemptRepository attemptRepository,
            AttemptSubmissionService submissionService,
            ILoggerFactory loggerFactory,
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

            if (request.Status is "closed")
            {
                var logger = loggerFactory.CreateLogger("SessionClose");
                var closedAt = DateTimeOffset.UtcNow.ToString("O");
                if (!await repository.TryCloseAsync(id, closedAt, cancellationToken))
                {
                    return Results.BadRequest(new { error = "Esta sesión ya está cerrada y no admite más cambios de estado." });
                }

                // Closure is committed; finish submitting the accepted attempts even if the caller disconnects.
                var attempts = await repository.GetInProgressAttemptIdsAsync(id, CancellationToken.None);
                var outcomes = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var attemptId in attempts)
                {
                    outcomes[attemptId] = await SubmitForCloseAsync(attemptId, id, attemptRepository, submissionService, logger, CancellationToken.None);
                }

                // Catch an attempt whose request observed the previous active state just before closure.
                var remainingAttempts = await repository.GetInProgressAttemptIdsAsync(id, CancellationToken.None);
                foreach (var attemptId in remainingAttempts)
                {
                    outcomes[attemptId] = await SubmitForCloseAsync(attemptId, id, attemptRepository, submissionService, logger, CancellationToken.None);
                }

                var submitted = outcomes.Values.Count(success => success);
                var failed = outcomes.Count - submitted;
                return Results.Ok(new { submitted, failed });
            }

            await repository.UpdateStatusAsync(id, request.Status, cancellationToken: cancellationToken);

            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, ISessionRepository repository, CancellationToken cancellationToken) =>
        {
            var session = await repository.GetByIdAsync(id, cancellationToken);
            if (session is null) return Results.NotFound();
            if (!await repository.DeleteIfNoAttemptsAsync(id, cancellationToken))
                return Results.Conflict(new { error = "No se puede descartar: ya ingresaron alumnos. Cerrala en su lugar." });
            return Results.NoContent();
        });

        return endpoints;
    }

    private static async Task<bool> SubmitForCloseAsync(
        string attemptId,
        string sessionId,
        IAttemptRepository attemptRepository,
        AttemptSubmissionService submissionService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await submissionService.SubmitAsync(attemptId, allowInactiveSession: true,
                cancellationToken: cancellationToken, submissionReason: "closed_by_teacher");
            if (result.Success) return true;

            if (await IsSubmittedAsync(attemptId, sessionId, attemptRepository, logger, cancellationToken)) return true;

            logger.LogError("Could not submit attempt {AttemptId} while closing session {SessionId}: {Error}", attemptId, sessionId, result.Error);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await IsSubmittedAsync(attemptId, sessionId, attemptRepository, logger, cancellationToken)) return true;

            logger.LogError(exception, "Could not submit attempt {AttemptId} while closing session {SessionId}.", attemptId, sessionId);
            return false;
        }
    }

    private static async Task<bool> IsSubmittedAsync(
        string attemptId,
        string sessionId,
        IAttemptRepository attemptRepository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await attemptRepository.GetByIdAsync(attemptId, cancellationToken))?.Status is "submitted";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not confirm submission for attempt {AttemptId} while closing session {SessionId}.", attemptId, sessionId);
            return false;
        }
    }

    private static bool IsAllowedTransition(string current, string next)
    {
        return (current, next) is
            ("active", "paused") or
            ("paused", "active") or
            ("active", "closed") or
            ("paused", "closed");
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

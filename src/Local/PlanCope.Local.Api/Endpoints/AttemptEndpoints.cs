using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Endpoints;

public static class AttemptEndpoints
{
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sessions/{sessionIdOrAccessCode}/student-resolution", async Task<IResult> (
            string sessionIdOrAccessCode,
            ResolveStudentRequest request,
            ISessionRepository sessionRepository,
            ILocalRosterRepository rosterRepository,
            IAttemptRepository attemptRepository,
            IDocumentHmacService documentHmacService,
            IStudentResolutionTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(sessionIdOrAccessCode, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { error = "Session not found." });
            }

            if (session.Status is not "active")
            {
                return Results.BadRequest(new { error = "Attempts can only start in active sessions." });
            }

            if (!IsNominal(session))
            {
                return Results.BadRequest(new { error = "This session does not use nominalization." });
            }

            if (string.IsNullOrWhiteSpace(request.Document))
            {
                return Results.BadRequest(new { error = "El DNI es obligatorio." });
            }

            LocalRosterStudentLookup? student;
            try
            {
                student = await rosterRepository.FindStudentAsync(
                    session.RosterSnapshotId!,
                    session.RosterSectionId!,
                    request.Document,
                    documentHmacService,
                    cancellationToken);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "El DNI no es válido." });
            }

            if (student is null)
            {
                return Results.NotFound(new { error = "No encontramos ese DNI en la sección seleccionada." });
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(5);
            var token = tokenService.CreateToken();
            await attemptRepository.CreateResolutionAsync(new StudentResolution(
                Guid.NewGuid().ToString(),
                session.Id,
                student.SnapshotId,
                student.SectionId,
                student.RosterStudentId,
                student.GePersonId,
                student.FirstName,
                student.LastName,
                student.DocumentLast4,
                tokenService.HashToken(token),
                expiresAt.ToString("O"),
                now.ToString("O")), cancellationToken);

            return Results.Ok(new ResolveStudentResponse(
                token,
                new ResolvedStudentDto(
                    $"{student.LastName}, {student.FirstName}",
                    MaskDocument(student.DocumentLast4),
                    student.FirstName,
                    student.LastName),
                expiresAt.ToString("O")));
        });

        endpoints.MapPost("/api/sessions/{sessionIdOrAccessCode}/attempts", async Task<IResult> (
            string sessionIdOrAccessCode,
            StartAttemptRequest? request,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            ILocalExamRepository examRepository,
            IStudentResolutionTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(sessionIdOrAccessCode, cancellationToken);

            if (session is null)
            {
                return Results.NotFound(new { error = "Session not found." });
            }

            if (session.Status is not "active")
            {
                return Results.BadRequest(new { error = "Attempts can only start in active sessions." });
            }

            if (IsNominal(session))
            {
                if (string.IsNullOrWhiteSpace(request?.ResolutionToken))
                {
                    return Results.BadRequest(new { error = "Debes confirmar tu identidad antes de comenzar." });
                }

                var nominalResult = await attemptRepository.StartNominalAttemptAsync(
                    session.Id,
                    tokenService.HashToken(request.ResolutionToken),
                    DateTimeOffset.UtcNow.ToString("O"),
                    cancellationToken);

                return nominalResult.Status switch
                {
                    NominalAttemptStartStatus.Started => await CreatedAttemptAsync(nominalResult.Attempt!, session.ExamVersionId, examRepository, cancellationToken),
                    NominalAttemptStartStatus.AttemptExists => Results.Conflict(new { error = "Ya existe un intento para este alumno en esta sesión." }),
                    NominalAttemptStartStatus.ResolutionExpired => Results.Conflict(new { error = "La confirmación expiró. Volvé a ingresar tu DNI." }),
                    NominalAttemptStartStatus.ResolutionUsed => Results.Conflict(new { error = "La confirmación ya fue utilizada." }),
                    _ => Results.Conflict(new { error = "La confirmación no es válida. Volvé a ingresar tu DNI." })
                };
            }

            var nextSequence = await attemptRepository.GetNextLocalSequenceAsync(session.Id, cancellationToken);
            var studentCode = $"AUTO-{nextSequence:0000}";

            var attempt = new StudentAttempt(
                Guid.NewGuid().ToString(),
                session.Id,
                studentCode,
                "in_progress",
                DateTimeOffset.UtcNow.ToString("O"),
                null,
                nextSequence,
                null);

            await attemptRepository.CreateAsync(attempt, cancellationToken);
            var blocks = await examRepository.GetBlocksAsync(session.ExamVersionId, cancellationToken);

            return Results.Created($"/api/attempts/{attempt.Id}", new { attempt, blocks });
        });

        endpoints.MapPut("/api/attempts/{attemptId}/answers", async (
            string attemptId,
            SaveAnswersRequest request,
            IAttemptRepository repository,
            CancellationToken cancellationToken) =>
        {
            var attempt = await repository.GetByIdAsync(attemptId, cancellationToken);

            if (attempt is null)
            {
                return Results.NotFound(new { error = "Attempt not found." });
            }

            if (attempt.Status is not "in_progress")
            {
                return Results.BadRequest(new { error = "Only in-progress attempts can be edited." });
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            var answers = request.Answers
                .Select(answer => new SubmissionAnswer(Guid.NewGuid().ToString(), attemptId, answer.BlockId, answer.Answer.GetRawText(), now))
                .ToList();

            await repository.UpsertAnswersAsync(attemptId, answers, cancellationToken);
            return Results.NoContent();
        });

        endpoints.MapPost("/api/attempts/{attemptId}/submit", async (
            string attemptId,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            CancellationToken cancellationToken) =>
        {
            var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken);

            if (attempt is null)
            {
                return Results.NotFound(new { error = "Attempt not found." });
            }

            if (attempt.Status is not "in_progress")
            {
                return Results.BadRequest(new { error = "Attempt has already been submitted or is not editable." });
            }

            var submittedAt = DateTimeOffset.UtcNow.ToString("O");
            var confirmationCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var answers = await attemptRepository.GetAnswersAsync(attemptId, cancellationToken);
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(attempt.DeliverySessionId, cancellationToken);
            var payloadJson = JsonSerializer.Serialize(new
            {
                attempt = attempt with
                {
                    Status = "submitted",
                    SubmittedAt = submittedAt,
                    ConfirmationCode = confirmationCode
                },
                answers,
                rosterSnapshotId = session?.RosterSnapshotId,
                rosterSectionId = session?.RosterSectionId
            });

            var submitted = await attemptRepository.SubmitWithOutboxAsync(attemptId, submittedAt, confirmationCode, new SyncOutbox(
                Guid.NewGuid().ToString(),
                SyncEventTypes.AttemptSubmitted,
                "student_attempt",
                attemptId,
                Guid.NewGuid().ToString(),
                payloadJson,
                "pending",
                0,
                null,
                null,
                submittedAt,
                null), cancellationToken);
            if (!submitted)
            {
                return Results.Conflict(new { error = "El intento ya fue enviado por otra operación." });
            }

            return Results.Ok(new SubmitAttemptResponse(attemptId, confirmationCode, submittedAt));
        });

        return endpoints;
    }

    private static bool IsNominal(LocalDeliverySession session) =>
        !string.IsNullOrWhiteSpace(session.RosterSnapshotId) && !string.IsNullOrWhiteSpace(session.RosterSectionId);

    private static string MaskDocument(string last4) => $"**.***.{last4}";

    private static async Task<IResult> CreatedAttemptAsync(
        StudentAttempt attempt,
        string examVersionId,
        ILocalExamRepository examRepository,
        CancellationToken cancellationToken)
    {
        var blocks = await examRepository.GetBlocksAsync(examVersionId, cancellationToken);
        return Results.Created($"/api/attempts/{attempt.Id}", new { attempt, blocks });
    }
}

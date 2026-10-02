using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Grading;

namespace PlanCope.Local.Api.Endpoints;

public static class AttemptEndpoints
{
    private const string SessionClosedErrorMessage = "La sesión ya está cerrada y no acepta más respuestas. Consultá con tu docente.";
    private const string SessionPausedErrorMessage = "La sesión está pausada. Esperá a que tu docente la reactive para continuar.";
    private static readonly SemaphoreSlim AttemptStartGate = new(1, 1);
    private static readonly SemaphoreSlim AttemptMutationGate = new(1, 1);

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
                return Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." });
            }

            if (session.Status is not "active")
            {
                return Results.BadRequest(new { error = "Esta sesión no está activa. Consultá con tu docente para poder ingresar." });
            }

            if (!IsNominal(session))
            {
                return Results.BadRequest(new { error = "Esta sesión no requiere confirmar identidad." });
            }

            if (string.IsNullOrWhiteSpace(request.Document))
            {
                return Results.BadRequest(new { error = "El DNI es obligatorio." });
            }

            LocalRosterStudentLookup? student;
            SessionExtraStudent? extraStudent = null;
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
                try
                {
                    extraStudent = await attemptRepository.FindExtraStudentAsync(
                        session.Id,
                        documentHmacService.ComputeHash(request.Document),
                        cancellationToken);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error = "El DNI no es válido." });
                }
                if (extraStudent is null)
                {
                    return Results.NotFound(new
                    {
                        kind = "not_found",
                        message = "¿Revisaste bien el DNI? No lo encontramos en la lista de esta sección.",
                        hint = "Si el problema sigue, avisá al docente o al operador del sistema."
                    });
                }
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(5);
            var token = tokenService.CreateToken();
            await attemptRepository.CreateResolutionAsync(new StudentResolution(
                Guid.NewGuid().ToString(),
                session.Id,
                student?.SnapshotId ?? session.RosterSnapshotId,
                student?.SectionId ?? session.RosterSectionId,
                student?.RosterStudentId,
                student?.GePersonId,
                extraStudent?.Id,
                student?.FirstName ?? extraStudent!.FirstName,
                student?.LastName ?? extraStudent!.LastName,
                student?.DocumentLast4 ?? extraStudent!.DocumentLast4,
                tokenService.HashToken(token),
                expiresAt.ToString("O"),
                now.ToString("O")), cancellationToken);

            var firstName = student?.FirstName ?? extraStudent!.FirstName;
            var lastName = student?.LastName ?? extraStudent!.LastName;
            var documentLast4 = student?.DocumentLast4 ?? extraStudent!.DocumentLast4;
            return Results.Ok(new ResolveStudentResponse(
                token,
                new ResolvedStudentDto(
                    $"{lastName}, {firstName}",
                    MaskDocument(documentLast4),
                    firstName,
                    lastName),
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
                return Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." });
            }

            await AttemptStartGate.WaitAsync(cancellationToken);
            try
            {
            if (session.Status is not "active")
            {
                return Results.BadRequest(new { error = "Esta sesión no está activa. Consultá con tu docente para poder ingresar." });
            }

            var resumeCredential = string.IsNullOrWhiteSpace(request?.ResumeCredential)
                ? tokenService.CreateToken()
                : request.ResumeCredential;
            if (resumeCredential.Length < 32)
                return Results.BadRequest(new { error = "La credencial de reanudación no es válida." });

            var existingForCredential = await attemptRepository.GetAttemptByResumeCredentialAsync(
                session.Id, tokenService.HashToken(resumeCredential), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            if (existingForCredential is not null)
                return await CreatedAttemptAsync(existingForCredential, session.ExamVersionId, examRepository, attemptRepository, tokenService, resumeCredential, cancellationToken);

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
                    NominalAttemptStartStatus.Started => await CreatedAttemptAsync(nominalResult.Attempt!, session.ExamVersionId, examRepository, attemptRepository, tokenService, resumeCredential, cancellationToken),
                    NominalAttemptStartStatus.SessionNotActive => Results.BadRequest(new { error = "Esta sesión no está activa. Consultá con tu docente para poder ingresar." }),
                    NominalAttemptStartStatus.AttemptExists when nominalResult.Attempt?.Status is "in_progress" => await CreatedAttemptAsync(nominalResult.Attempt!, session.ExamVersionId, examRepository, attemptRepository, tokenService, resumeCredential, cancellationToken),
                    NominalAttemptStartStatus.AttemptExists => Results.Conflict(new { error = "Este intento ya fue entregado." }),
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

            if (!await attemptRepository.CreateIfSessionActiveAsync(attempt, cancellationToken))
            {
                return Results.BadRequest(new { error = "Esta sesión no está activa. Consultá con tu docente para poder ingresar." });
            }
            return await CreatedAttemptAsync(attempt, session.ExamVersionId, examRepository, attemptRepository, tokenService, resumeCredential, cancellationToken);
            }
            finally
            {
                AttemptStartGate.Release();
            }
        });

        endpoints.MapGet("/api/sessions/{sessionIdOrAccessCode}/attempts/restore", async Task<IResult> (
            string sessionIdOrAccessCode,
            HttpRequest httpRequest,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            IStudentResolutionTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadCredential(httpRequest, out var credential)) return Unauthorized();
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(sessionIdOrAccessCode, cancellationToken);
            if (session is null) return Results.NotFound(new { error = "No encontramos esa sesión." });
            if (session.Status is "closed") return Results.StatusCode(StatusCodes.Status410Gone);
            var attempt = await attemptRepository.GetAttemptByResumeCredentialAsync(session.Id, tokenService.HashToken(credential), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            return attempt is null
                ? Results.Json(new { kind = "resume_pending", error = "La creación del intento aún está terminando." }, statusCode: StatusCodes.Status425TooEarly)
                : Results.Ok(new { attemptId = attempt.Id });
        });

        endpoints.MapGet("/api/attempts/{attemptId}/restore", async Task<IResult> (
            string attemptId,
            HttpRequest httpRequest,
            IAttemptRepository repository,
            ISessionRepository sessionRepository,
            ILocalExamRepository examRepository,
            IStudentResolutionTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadCredential(httpRequest, out var credential)) return Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var attempt = await repository.GetAuthorizedAttemptAsync(attemptId, tokenService.HashToken(credential), now.ToString("O"), cancellationToken);
            if (attempt is null) return Unauthorized();
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(attempt.DeliverySessionId, cancellationToken);
            if (session is null || session.Status is "closed")
            {
                await repository.RevokeResumeCredentialAsync(attemptId, cancellationToken);
                return Results.StatusCode(StatusCodes.Status410Gone);
            }
            var answers = await repository.GetAnswersAsync(attemptId, cancellationToken);
            var blocks = await examRepository.GetBlocksAsync(session.ExamVersionId, cancellationToken);
            return Results.Ok(new
            {
                attempt,
                blocks,
                answers = answers.Select(answer => new
                {
                    blockId = answer.BlockId,
                    answer = JsonDocument.Parse(answer.AnswerJson).RootElement.Clone(),
                    revision = answer.Revision
                }),
                sessionStatus = session.Status
            });
        });

        endpoints.MapPut("/api/attempts/{attemptId}/answers", async (
            string attemptId,
            SaveAnswersRequest request,
            HttpRequest httpRequest,
            IAttemptRepository repository,
            ISessionRepository sessionRepository,
            IStudentResolutionTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadCredential(httpRequest, out var credential)) return Unauthorized();
            if (request.Revision < 1) return Results.BadRequest(new { error = "La revisión de respuestas no es válida.", kind = "invalid_revision" });
            await AttemptMutationGate.WaitAsync(cancellationToken);
            try
            {
            var attempt = await repository.GetAuthorizedAttemptAsync(attemptId, tokenService.HashToken(credential), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

            if (attempt is null)
            {
                return Results.NotFound(new { error = "No encontramos ese intento. Volvé a ingresar con tu código de sesión." });
            }

            if (attempt.Status is not "in_progress")
            {
                return Results.BadRequest(new { error = "Este intento ya no admite cambios." });
            }

            var session = await sessionRepository.GetByIdOrAccessCodeAsync(attempt.DeliverySessionId, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." });
            }

            if (session.Status is "closed")
            {
                return Results.BadRequest(new { error = SessionClosedErrorMessage });
            }

            if (session.Status is "paused")
            {
                return Results.BadRequest(new { error = SessionPausedErrorMessage });
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            var answers = request.Answers
                .Select(answer => new SubmissionAnswer(Guid.NewGuid().ToString(), attemptId, answer.BlockId, answer.Answer.GetRawText(), now))
                .ToList();

            if (!await repository.ReconcileAnswersAsync(attemptId, answers, request.Revision, cancellationToken))
                return Results.Conflict(new { error = "Este intento ya no admite cambios." });
            return Results.NoContent();
            }
            finally
            {
                AttemptMutationGate.Release();
            }
        });

        endpoints.MapPost("/api/attempts/{attemptId}/submit", async (
            string attemptId,
            HttpRequest httpRequest,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            IStudentResolutionTokenService tokenService,
            AttemptSubmissionService submissionService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadCredential(httpRequest, out var credential)) return Unauthorized();
            await AttemptMutationGate.WaitAsync(cancellationToken);
            try
            {
            var attempt = await attemptRepository.GetAuthorizedAttemptAsync(attemptId, tokenService.HashToken(credential), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

            if (attempt is null)
            {
                return Results.NotFound(new { error = "No encontramos ese intento. Volvé a ingresar con tu código de sesión." });
            }

            if (attempt.Status is not "in_progress")
            {
                return Results.BadRequest(new { error = "Este intento ya fue enviado. Si creés que es un error, avisá al docente." });
            }

            var session = await sessionRepository.GetByIdOrAccessCodeAsync(attempt.DeliverySessionId, cancellationToken);
            if (session is null)
            {
                return Results.NotFound(new { error = "No encontramos esa sesión. Verificá el código con tu docente." });
            }

            if (session.Status is "closed")
            {
                return Results.BadRequest(new { error = SessionClosedErrorMessage });
            }

            if (session.Status is "paused")
            {
                return Results.BadRequest(new { error = SessionPausedErrorMessage });
            }

            var submitted = await submissionService.SubmitAsync(attemptId, cancellationToken: cancellationToken);
            if (!submitted.Success) return Results.Conflict(new { error = "El intento ya fue enviado por otra operación." });
            return Results.Ok(submitted.Response);
            }
            finally
            {
                AttemptMutationGate.Release();
            }
        });

        return endpoints;
    }

    private static bool IsNominal(LocalDeliverySession session) =>
        !string.IsNullOrWhiteSpace(session.RosterSnapshotId) && !string.IsNullOrWhiteSpace(session.RosterSectionId);

    private static string MaskDocument(string last4) => $"**.***.{last4}";

    private static bool TryReadCredential(HttpRequest request, out string credential)
    {
        credential = string.Empty;
        var header = request.Headers["Authorization"].ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        credential = header[7..].Trim();
        return credential.Length >= 32;
    }

    private static IResult Unauthorized() => Results.Json(
        new { kind = "resume_credential_invalid", error = "La sesión de examen venció o ya no está disponible. Volvé a ingresar tu DNI." },
        statusCode: StatusCodes.Status401Unauthorized);

    private static async Task<IResult> CreatedAttemptAsync(
        StudentAttempt attempt,
        string examVersionId,
        ILocalExamRepository examRepository,
        IAttemptRepository attemptRepository,
        IStudentResolutionTokenService tokenService,
        string credential,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(4).ToString("O");
        await attemptRepository.CreateResumeCredentialAsync(attempt.Id, attempt.DeliverySessionId, tokenService.HashToken(credential), expiresAt, now.ToString("O"), cancellationToken);
        var blocks = await examRepository.GetBlocksAsync(examVersionId, cancellationToken);
        return Results.Created($"/api/attempts/{attempt.Id}", new { attempt, blocks, resumeCredential = credential, credentialExpiresAt = expiresAt });
    }
}

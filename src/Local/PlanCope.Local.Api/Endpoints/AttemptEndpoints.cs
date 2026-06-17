using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Endpoints;

public static class AttemptEndpoints
{
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sessions/{sessionIdOrAccessCode}/attempts", async (
            string sessionIdOrAccessCode,
            ISessionRepository sessionRepository,
            IAttemptRepository attemptRepository,
            ILocalExamRepository examRepository,
            CancellationToken cancellationToken) =>
        {
            var session = await sessionRepository.GetByIdOrAccessCodeAsync(sessionIdOrAccessCode, cancellationToken);

            if (session is null)
            {
                return Results.NotFound();
            }

            if (session.Status is not "active")
            {
                return Results.BadRequest(new { error = "Attempts can only start in active sessions." });
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

            return Results.Created($"/api/attempts/{attempt.Id}", new
            {
                attempt,
                blocks
            });
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
                return Results.NotFound();
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
            IAttemptRepository attemptRepository,
            IOutboxRepository outboxRepository,
            CancellationToken cancellationToken) =>
        {
            var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken);

            if (attempt is null)
            {
                return Results.NotFound();
            }

            if (attempt.Status is not "in_progress")
            {
                return Results.BadRequest(new { error = "Attempt has already been submitted or is not editable." });
            }

            var submittedAt = DateTimeOffset.UtcNow.ToString("O");
            var confirmationCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var answers = await attemptRepository.GetAnswersAsync(attemptId, cancellationToken);
            var payloadJson = JsonSerializer.Serialize(new
            {
                attempt = attempt with
                {
                    Status = "submitted",
                    SubmittedAt = submittedAt,
                    ConfirmationCode = confirmationCode
                },
                answers
            });

            await attemptRepository.SubmitAsync(attemptId, submittedAt, confirmationCode, cancellationToken);
            await outboxRepository.InsertAsync(new SyncOutbox(
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

            return Results.Ok(new SubmitAttemptResponse(attemptId, confirmationCode, submittedAt));
        });

        return endpoints;
    }
}

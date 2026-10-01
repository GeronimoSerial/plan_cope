using System.Text.Json;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Grading;

namespace PlanCope.Local.Api.Services;

/// <summary>Single submission path for operator submits and expiry auto-finalization.</summary>
public sealed class AttemptSubmissionService(
    IAttemptRepository attemptRepository,
    ISessionRepository sessionRepository,
    ILocalExamRepository examRepository,
    IStatsRollupRepository statsRollupRepository,
    ILocalSqliteConnectionFactory connectionFactory,
    ILogger<AttemptSubmissionService> logger)
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AttemptSubmitResult> SubmitAsync(string attemptId, bool allowInactiveSession = false, CancellationToken cancellationToken = default, string? submissionReason = null)
    {
        var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken);
        if (attempt is null) return new(false, null, "Attempt not found.");
        if (attempt.Status != "in_progress") return new(false, null, "Attempt is already submitted.");
        var session = await sessionRepository.GetByIdOrAccessCodeAsync(attempt.DeliverySessionId, cancellationToken);
        if (session is null) return new(false, null, "Session not found.");
        if (!allowInactiveSession && session.Status is "closed" or "paused")
            return new(false, null, "Session is not accepting submissions.");

        var submittedAt = DateTimeOffset.UtcNow.ToString("O");
        var confirmationCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var answers = await attemptRepository.GetAnswersAsync(attemptId, cancellationToken);
        var examVersion = await examRepository.GetByIdAsync(session.ExamVersionId, cancellationToken);
        var course = await GetRosterCourseAsync(session.RosterSectionId, cancellationToken);
        var blocks = examVersion is null ? Array.Empty<LocalExamBlock>() : await examRepository.GetBlocksAsync(examVersion.Id, cancellationToken);
        var answerKeys = examVersion is null ? Array.Empty<LocalAnswerKey>() : await examRepository.GetAnswerKeysAsync(examVersion.Id, cancellationToken);
        var grading = GradeAttempt(examVersion, blocks, answerKeys, answers);
        var payload = JsonSerializer.Serialize(new
        {
            attempt = attempt with { Status = "submitted", SubmittedAt = submittedAt, ConfirmationCode = confirmationCode },
            answers,
            rosterSnapshotId = session.RosterSnapshotId,
            rosterSectionId = session.RosterSectionId,
            examVersionRemoteId = examVersion?.RemoteExamVersionId,
            deliverySession = new
            {
                id = session.Id,
                schoolCue = session.SchoolCode,
                schoolYear = session.SchoolYear,
                course,
                sectionId = session.RosterSectionId,
                examVersionId = examVersion?.RemoteExamVersionId,
                startedAt = session.StartAt,
                closedAt = session.EndAt,
                status = session.Status
            }
        }, SyncJsonOptions);
        var submitted = await attemptRepository.SubmitWithOutboxAsync(attemptId, submittedAt, confirmationCode, new SyncOutbox(
            Guid.NewGuid().ToString(), SyncEventTypes.AttemptSubmitted, "student_attempt", attemptId, Guid.NewGuid().ToString(),
            payload, "pending", 0, null, null, submittedAt, null), grading, cancellationToken, submissionReason);
        if (!submitted) return new(false, null, "Attempt was submitted concurrently.");
        await statsRollupRepository.UpsertForAttemptAsync(attemptId, cancellationToken);
        return new(true, new SubmitAttemptResponse(attemptId, confirmationCode, submittedAt), null);
    }

    public async Task<bool> FinalizeUnsubmittedAttemptsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var ids = (await Dapper.SqlMapper.QueryAsync<string>(connection, new Dapper.CommandDefinition(
            "SELECT id FROM student_attempts WHERE submitted_at IS NULL ORDER BY started_at;", cancellationToken: cancellationToken))).ToArray();
        var unresolved = false;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptSubmitResult result;
            try
            {
                result = await SubmitAsync(id, allowInactiveSession: true, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Normal submission failed while finalizing attempt {AttemptId}; preserving its raw answers.", id);
                result = new(false, null, exception.Message);
            }
            if (result.Success) continue;

            try
            {
                if (!await PreserveRawAttemptAsync(id, cancellationToken))
                {
                    var current = await attemptRepository.GetByIdAsync(id, cancellationToken);
                    if (current?.SubmittedAt is null)
                    {
                        unresolved = true;
                        logger.LogError("Attempt {AttemptId} could not be written to the outbox; its exam data must be retained during expiry.", id);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unresolved = true;
                logger.LogError(exception, "Could not preserve raw answers for attempt {AttemptId}; its exam data must be retained during expiry.", id);
            }
        }
        return unresolved;
    }

    private async Task<bool> PreserveRawAttemptAsync(string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.SubmittedAt is not null) return false;
        var session = await sessionRepository.GetByIdAsync(attempt.DeliverySessionId, cancellationToken);
        var course = await GetRosterCourseAsync(session?.RosterSectionId, cancellationToken);
        var answers = await attemptRepository.GetAnswersAsync(attemptId, cancellationToken);
        var submittedAt = DateTimeOffset.UtcNow.ToString("O");
        var confirmationCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var normalizedAttempt = attempt with { Status = "submitted", SubmittedAt = submittedAt, ConfirmationCode = confirmationCode };
        var payload = JsonSerializer.Serialize(new
        {
            attempt = normalizedAttempt,
            answers,
            rosterSnapshotId = session?.RosterSnapshotId,
            rosterSectionId = session?.RosterSectionId,
            examVersionRemoteId = (string?)null,
            deliverySession = session is null ? null : new
            {
                id = session.Id,
                schoolCue = session.SchoolCode,
                schoolYear = session.SchoolYear,
                course,
                sectionId = session.RosterSectionId,
                examVersionId = (string?)null,
                startedAt = session.StartAt,
                closedAt = session.EndAt,
                status = session.Status
            }
        }, SyncJsonOptions);
        var outbox = new SyncOutbox(Guid.NewGuid().ToString(), SyncEventTypes.AttemptSubmitted,
            "student_attempt", attemptId, Guid.NewGuid().ToString(), payload, "pending", 0, null, null, submittedAt, null);
        var preserved = await attemptRepository.PreserveUnsubmittedWithOutboxAsync(attemptId, submittedAt,
            confirmationCode, outbox, cancellationToken);
        if (preserved)
            await statsRollupRepository.UpsertForAttemptAsync(attemptId, cancellationToken);
        return preserved;
    }

    private static GradingOutcome GradeAttempt(LocalExamVersion? examVersion, IReadOnlyList<LocalExamBlock> blocks,
        IReadOnlyList<LocalAnswerKey> answerKeys, IReadOnlyList<SubmissionAnswer> submittedAnswers)
    {
        var gradedAt = DateTimeOffset.UtcNow.ToString("O");
        if (examVersion is null) return GradingOutcome.Ungradable(gradedAt);
        var blocksById = blocks.ToDictionary(block => block.Id);
        var answerKeyByRemoteBlock = answerKeys.ToDictionary(key => key.RemoteBlockId);
        var gradableBlocks = blocks.Select(block =>
        {
            var key = answerKeyByRemoteBlock.GetValueOrDefault(block.RemoteBlockId);
            return GradingJsonMapper.MapBlock(block.Id, block.BlockType,
                key is null ? null : (decimal?)key.ScoreValue, ParseJsonElement(key?.CorrectAnswerJson), ParseJsonElement(block.ConfigJson));
        }).ToList();
        var mappedAnswers = new Dictionary<string, SubmittedAnswer>();
        foreach (var submitted in submittedAnswers)
        {
            if (!blocksById.TryGetValue(submitted.BlockId, out var block)) continue;
            var mapped = GradingJsonMapper.MapSubmittedAnswer(block.BlockType, ParseJsonElement(submitted.AnswerJson));
            if (mapped is not null) mappedAnswers[submitted.BlockId] = mapped;
        }
        try
        {
            var result = new GradingEngine().Grade(new ExamVersion
            {
                ExamVersionId = examVersion.Id,
                Blocks = gradableBlocks
            }, mappedAnswers);
            return GradingOutcome.Graded(result, gradedAt);
        }
        catch (UngradableExamException) { return GradingOutcome.Ungradable(gradedAt); }
    }

    private async Task<string?> GetRosterCourseAsync(string? sectionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sectionId)) return null;
        using var connection = connectionFactory.CreateOpenConnection();
        return await Dapper.SqlMapper.QuerySingleOrDefaultAsync<string>(connection, new Dapper.CommandDefinition(
            "SELECT course FROM local_roster_sections WHERE id = @SectionId LIMIT 1;",
            new { SectionId = sectionId },
            cancellationToken: cancellationToken));
    }

    private static JsonElement? ParseJsonElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed record AttemptSubmitResult(bool Success, SubmitAttemptResponse? Response, string? Error);

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Grading;
using GradingExamVersion = PlanCope.Shared.Grading.ExamVersion;

namespace PlanCope.Central.Api.Services;

public sealed class CentralAttemptGradingService(
    PlanCopeDbContext dbContext,
    CentralStatsRollupService statsRollupService,
    ILogger<CentralAttemptGradingService>? logger = null)
{
    private static readonly JsonSerializerOptions BlocksJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Grades from Central's exam definition and updates the attempt's latest result. Setting
    /// <paramref name="updateRollup"/> false is used during a full rebuild so existing totals
    /// are never incremented before being reconstructed.
    /// </summary>
    public async Task RecomputeAsync(
        string receivedAttemptId,
        string? examVersionRemoteId,
        IReadOnlyList<ReceivedSubmissionAnswer> answers,
        CancellationToken cancellationToken,
        bool updateRollup = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(examVersionRemoteId))
            {
                await StoreUngradableAsync(receivedAttemptId, "The attempt payload did not include an exam version.", cancellationToken);
                return;
            }

            var examVersion = await dbContext.ExamVersions
                .SingleOrDefaultAsync(version => version.Id == examVersionRemoteId, cancellationToken);
            if (examVersion is null)
            {
                await StoreUngradableAsync(receivedAttemptId, $"Exam version '{examVersionRemoteId}' was not found in Central.", cancellationToken);
                return;
            }

            var blocks = await dbContext.ExamBlocks
                .Where(block => block.ExamVersionId == examVersion.Id)
                .OrderBy(block => block.OrderIndex)
                .ToListAsync(cancellationToken);
            var blockIds = blocks.Select(static block => block.Id).ToList();
            var answerKeys = await dbContext.AnswerKeys
                .Where(key => blockIds.Contains(key.ExamBlockId))
                .ToListAsync(cancellationToken);
            var answerKeyByBlockId = answerKeys.ToDictionary(static key => key.ExamBlockId);

            var gradableBlocks = new List<GradableBlock>(blocks.Count);
            foreach (var block in blocks)
            {
                var answerKey = answerKeyByBlockId.GetValueOrDefault(block.Id);
                gradableBlocks.Add(GradingJsonMapper.MapBlock(
                    block.Id,
                    block.BlockType,
                    answerKey?.ScoreValue,
                    answerKey is null ? null : answerKey.CorrectAnswer.RootElement.Clone(),
                    block.Config.RootElement));
            }

            var blocksById = blocks.ToDictionary(static block => block.Id);
            var submitted = new Dictionary<string, SubmittedAnswer>();
            foreach (var received in answers)
            {
                if (!blocksById.TryGetValue(received.BlockId, out var block)) continue;
                var mapped = GradingJsonMapper.MapSubmittedAnswer(block.BlockType, received.Answer.RootElement);
                if (mapped is not null) submitted[received.BlockId] = mapped;
            }

            var result = new GradingEngine().Grade(new GradingExamVersion
            {
                ExamVersionId = examVersion.Id,
                Blocks = gradableBlocks
            }, submitted);

            await StoreGradeResultAsync(new CentralAttemptResult(
                await ExistingResultIdAsync(receivedAttemptId, cancellationToken),
                receivedAttemptId,
                result.GradingSchemaVersion,
                result.ScoringPolicy?.ToString(),
                "graded",
                result.Score,
                result.ScoreMax,
                JsonDocument.Parse(JsonSerializer.Serialize(result.Blocks, BlocksJsonOptions)),
                DateTimeOffset.UtcNow), cancellationToken);

            if (updateRollup)
            {
                await statsRollupService.UpsertForAttemptAsync(receivedAttemptId, result, examVersion.Id, cancellationToken);
            }
        }
        catch (UngradableExamException exception)
        {
            await StoreUngradableAsync(receivedAttemptId, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Central could not grade attempt {AttemptId}; it is stored as ungradable.", receivedAttemptId);
            await StoreUngradableAsync(receivedAttemptId, $"Grading failed: {exception.Message}", cancellationToken);
        }
    }

    private async Task StoreUngradableAsync(string receivedAttemptId, string reason, CancellationToken cancellationToken)
    {
        logger?.LogWarning("Central attempt {AttemptId} is ungradable: {Reason}", receivedAttemptId, reason);
        await StoreGradeResultAsync(new CentralAttemptResult(
            await ExistingResultIdAsync(receivedAttemptId, cancellationToken),
            receivedAttemptId,
            GradingSchemaVersion.Current,
            null,
            "ungradable",
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            reason), cancellationToken);
    }

    private async Task<string> ExistingResultIdAsync(string receivedAttemptId, CancellationToken cancellationToken) =>
        await dbContext.CentralAttemptResults
            .Where(result => result.ReceivedStudentAttemptId == receivedAttemptId && result.GradingSchemaVersion == GradingSchemaVersion.Current)
            .Select(result => result.Id)
            .SingleOrDefaultAsync(cancellationToken) ?? Guid.NewGuid().ToString("N");

    private async Task StoreGradeResultAsync(CentralAttemptResult result, CancellationToken cancellationToken)
    {
        var existing = await dbContext.CentralAttemptResults.FindAsync([result.Id], cancellationToken);
        if (existing is null) dbContext.CentralAttemptResults.Add(result);
        else dbContext.Entry(existing).CurrentValues.SetValues(result);
    }
}

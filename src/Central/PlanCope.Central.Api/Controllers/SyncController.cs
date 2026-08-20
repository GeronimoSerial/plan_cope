using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Sync;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class SyncController(PlanCopeDbContext dbContext) : ControllerBase
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("pull")]
    public async Task<ActionResult<PullResponse>> Pull([FromQuery] string nodeId, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            ModelState.AddModelError(nameof(nodeId), "nodeId is required.");
            return ValidationProblem(ModelState);
        }

        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var cursorTicks = ParseCursor(cursor);
        var query = dbContext.PublicationPackages
            .Where(x => x.Status == "Published" && x.PublishedAt != null && x.PublishedAt.Value.UtcTicks > cursorTicks)
            .OrderBy(x => x.PublishedAt)
            .ThenBy(x => x.Id);

        var packages = await query.Take(normalizedLimit + 1).ToListAsync(cancellationToken);
        var hasMore = packages.Count > normalizedLimit;
        var page = packages.Take(normalizedLimit).ToList();
        var items = new List<SyncItem>(page.Count);

        foreach (var package in page)
        {
            var payload = await BuildPayloadAsync(package, cancellationToken);
            items.Add(new SyncItem(
                "publication_package",
                package.Id,
                "upsert",
                JsonSerializer.SerializeToElement(payload),
                (package.PublishedAt ?? package.CreatedAt).ToString("O"),
                package.Checksum));
        }

        var nextCursor = page.Count == 0
            ? (cursor ?? "0")
            : (page[^1].PublishedAt ?? page[^1].CreatedAt).UtcTicks.ToString();

        return Ok(new PullResponse(
            items,
            nextCursor,
            hasMore,
            page.ToDictionary(static package => package.Id, static package => package.Checksum)));
    }

    [HttpPost("push")]
    public async Task<ActionResult<PushResponse>> Push(
        [FromBody] PushRequest? request,
        [FromHeader(Name = "X-Node-Id")] string? nodeHeader,
        [FromServices] IValidator<PushRequest> validator,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { error = "A push request is required." });
        }

        if (string.IsNullOrWhiteSpace(nodeHeader) ||
            !string.Equals(nodeHeader.Trim(), request.NodeId?.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { error = "X-Node-Id must match nodeId." });
        }

        if (request.Items.Count > 200)
        {
            return BadRequest(new { error = "A push request cannot contain more than 200 items." });
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var nodeId = request.NodeId?.Trim() ?? string.Empty;
        var results = new List<PushItemResult>(request.Items.Count);
        var keysInRequest = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!keysInRequest.Add(item.IdempotencyKey))
            {
                results.Add(new PushItemResult(item.IdempotencyKey, "failed", "Duplicate idempotency key in request."));
                continue;
            }

            var result = await AcceptItemAsync(nodeId, item, cancellationToken);
            results.Add(result);
        }

        var accepted = results.Count(static result => result.Status is "accepted" or "duplicate");
        return Ok(new PushResponse(accepted, results.Count - accepted, results));
    }

    private async Task<PushItemResult> AcceptItemAsync(string nodeId, PushItem item, CancellationToken cancellationToken)
    {
        var existing = await dbContext.SyncInbox
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == item.IdempotencyKey, cancellationToken);
        var policyResult = SyncPushPolicy.Evaluate(item, existing?.Payload.RootElement);
        if (existing is not null || policyResult.Status is not "accepted")
        {
            return policyResult;
        }

        try
        {
            using var payload = JsonDocument.Parse(item.Payload.GetRawText());
            if (ContainsForbiddenNominalProperty(payload.RootElement))
            {
                return new PushItemResult(item.IdempotencyKey, "failed", "The payload contains a prohibited document or token field.");
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.SyncInbox.Add(new SyncInbox(
                Guid.NewGuid().ToString("N"),
                nodeId,
                item.EventType,
                item.AggregateType,
                item.AggregateId,
                item.IdempotencyKey,
                JsonDocument.Parse(item.Payload.GetRawText()),
                "processed",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

            if (item.EventType is SyncEventTypes.AttemptSubmitted)
            {
                await AddAttemptAsync(item, payload.RootElement, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "accepted", null);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var nowExisting = await dbContext.SyncInbox
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == item.IdempotencyKey, cancellationToken);
            if (nowExisting is null)
            {
                return new PushItemResult(item.IdempotencyKey, "failed", "The item could not be persisted.");
            }

            return SyncPushPolicy.Evaluate(item, nowExisting.Payload.RootElement);
        }
        catch (JsonException exception)
        {
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "failed", exception.Message);
        }
    }

    private async Task AddAttemptAsync(PushItem item, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("attempt", out var attemptElement))
        {
            throw new JsonException("attempt_submitted payload must contain an attempt.");
        }

        var attempt = attemptElement.Deserialize<StudentAttempt>(SyncJsonOptions)
            ?? throw new JsonException("The attempt payload is invalid.");
        if (!string.Equals(attempt.Id, item.AggregateId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.StudentCode))
        {
            throw new JsonException("The attempt aggregate does not match the payload.");
        }

        DateTimeOffset? startedAt = ParseOptionalDate(attempt.StartedAt);
        DateTimeOffset? submittedAt = ParseOptionalDate(attempt.SubmittedAt);
        DateTimeOffset? verifiedAt = ParseOptionalDate(attempt.VerifiedAt);
        var receivedAt = DateTimeOffset.UtcNow;
        dbContext.ReceivedStudentAttempts.Add(new ReceivedStudentAttempt(
            Guid.NewGuid().ToString("N"),
            attempt.Id,
            attempt.DeliverySessionId,
            attempt.StudentCode,
            attempt.Status,
            startedAt,
            submittedAt,
            receivedAt,
            item.IdempotencyKey,
            receivedAt,
            attempt.GePersonId,
            attempt.RosterStudentId,
            ReadOptionalString(payload, "rosterSnapshotId"),
            ReadOptionalString(payload, "rosterSectionId"),
            attempt.StudentFirstName,
            attempt.StudentLastName,
            attempt.DocumentLast4,
            attempt.VerificationSource,
            verifiedAt));

        if (!payload.TryGetProperty("answers", out var answersElement) || answersElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var answerElement in answersElement.EnumerateArray())
        {
            var answer = answerElement.Deserialize<SubmissionAnswer>(SyncJsonOptions)
                ?? throw new JsonException("The answer payload is invalid.");
            if (string.IsNullOrWhiteSpace(answer.BlockId))
            {
                throw new JsonException("An answer is missing its block id.");
            }

            dbContext.ReceivedSubmissionAnswers.Add(new ReceivedSubmissionAnswer(
                Guid.NewGuid().ToString("N"),
                attempt.Id,
                answer.BlockId,
                JsonDocument.Parse(answer.AnswerJson),
                receivedAt));
        }
    }

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ContainsForbiddenNominalProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsForbiddenPropertyName(property.Name))
                {
                    return true;
                }

                if (ContainsForbiddenNominalProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsForbiddenNominalProperty);
        }

        return false;
    }

    private static bool IsForbiddenPropertyName(string name) =>
        name.Equals("document", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("documentHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("tokenHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("resolutionToken", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dni", StringComparison.OrdinalIgnoreCase);

    private async Task<PublishedExamPackageDto> BuildPayloadAsync(PublicationPackage package, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleAsync(x => x.Id == package.ExamVersionId, cancellationToken);
        var exam = await dbContext.Exams.SingleAsync(x => x.Id == version.ExamId, cancellationToken);
        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == version.Id)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);
        var blockIds = blocks.Select(static x => x.Id).ToList();
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blockIds.Contains(x.ExamBlockId))
            .OrderBy(x => x.ExamBlockId)
            .ToListAsync(cancellationToken);
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == version.Id)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var targets = await dbContext.PublicationTargets
            .Where(x => x.PublicationPackageId == package.Id)
            .OrderBy(x => x.TargetType)
            .ThenBy(x => x.TargetId)
            .ToListAsync(cancellationToken);

        return new PublishedExamPackageDto(
            package.Id,
            exam.Id,
            version.Id,
            exam.Code,
            exam.Title,
            version.VersionNumber,
            version.SchemaVersion,
            package.Checksum,
            ToJsonElement(version.Metadata),
            blocks.Select(ToDto).ToList(),
            answerKeys.Select(ToDto).ToList(),
            assets.Select(ToPublishedDto).ToList(),
            targets.Select(static target => new PublicationTargetDto(target.TargetType, target.TargetId)).ToList());
    }

    private static long ParseCursor(string? cursor)
    {
        return long.TryParse(cursor, out var ticks) ? ticks : 0;
    }

    private static BlockDto ToDto(ExamBlock block)
    {
        return new BlockDto(
            block.Id,
            block.ExamVersionId,
            block.OrderIndex,
            block.BlockType,
            block.Title,
            block.Description,
            block.Config.RootElement.Clone(),
            block.Validation?.RootElement.Clone());
    }

    private static AnswerKeyDto ToDto(AnswerKey answerKey)
    {
        return new AnswerKeyDto(
            answerKey.Id,
            answerKey.ExamBlockId,
            answerKey.CorrectAnswer.RootElement.Clone(),
            answerKey.ScoreValue,
            answerKey.Metadata?.RootElement.Clone());
    }

    private static PublishedAssetDto ToPublishedDto(ExamAsset asset)
    {
        var contentBase64 = asset.StoragePath.StartsWith("base64:", StringComparison.Ordinal)
            ? asset.StoragePath["base64:".Length..]
            : string.Empty;

        return new PublishedAssetDto(asset.Id, asset.ExamVersionId, asset.FileName, asset.MimeType, asset.SizeBytes, asset.Checksum, contentBase64);
    }

    private static JsonElement? ToJsonElement(JsonDocument? document)
    {
        return document?.RootElement.Clone();
    }
}

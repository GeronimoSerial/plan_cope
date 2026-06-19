using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class SyncController(PlanCopeDbContext dbContext) : ControllerBase
{
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

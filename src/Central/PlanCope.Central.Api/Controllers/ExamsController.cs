using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/exams")]
public sealed class ExamsController(
    PlanCopeDbContext dbContext,
    IValidator<Exam> examValidator,
    IValidator<ExamVersion> versionValidator,
    IValidator<ExamBlock> blockValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExamSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var exams = await dbContext.Exams
            .Where(x => x.DeletedAt == null)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var versionCounts = await dbContext.ExamVersions
            .GroupBy(x => x.ExamId)
            .Select(x => new { ExamId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ExamId, x => x.Count, cancellationToken);

        return Ok(exams.Select(x => new ExamSummaryDto(
            x.Id,
            x.Code,
            x.Title,
            x.Level,
            x.Area,
            x.Subject,
            x.Status,
            versionCounts.GetValueOrDefault(x.Id))).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ExamSummaryDto>> Create(CreateExamRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var exam = new Exam(
            NewId(),
            request.Code.Trim(),
            request.Title.Trim(),
            request.Description,
            request.Level,
            request.Area,
            request.Subject,
            "Draft",
            null,
            now,
            now);

        var validation = await examValidator.ValidateAsync(exam, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var codeExists = await dbContext.Exams.AnyAsync(x => x.Code == exam.Code, cancellationToken);
        if (codeExists)
        {
            ModelState.AddModelError(nameof(request.Code), "An exam with this code already exists.");
            return ValidationProblem(ModelState);
        }

        dbContext.Exams.Add(exam);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new { id = exam.Id }, new ExamSummaryDto(
            exam.Id,
            exam.Code,
            exam.Title,
            exam.Level,
            exam.Area,
            exam.Subject,
            exam.Status,
            0));
    }

    [HttpPost("{examId}/versions")]
    public async Task<ActionResult<ExamVersionDto>> CreateVersion(string examId, CreateExamVersionRequest request, CancellationToken cancellationToken)
    {
        var examExists = await dbContext.Exams.AnyAsync(x => x.Id == examId && x.DeletedAt == null, cancellationToken);
        if (!examExists)
        {
            return NotFound();
        }

        var latestVersion = await dbContext.ExamVersions
            .Where(x => x.ExamId == examId)
            .MaxAsync(x => (int?)x.VersionNumber, cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var version = new ExamVersion(
            NewId(),
            examId,
            latestVersion + 1,
            request.SchemaVersion,
            "Draft",
            ToJsonDocument(request.Metadata),
            null,
            null,
            null,
            null,
            null,
            now,
            now);

        var validation = await versionValidator.ValidateAsync(version, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        dbContext.ExamVersions.Add(version);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Conflict("A version was created concurrently. Retry the request.");
        }

        return CreatedAtAction(nameof(GetVersion), new { versionId = version.Id }, ToDto(version, [], [], []));
    }

    [HttpGet("{examId}/versions")]
    public async Task<ActionResult<IReadOnlyList<ExamVersionDto>>> ListVersions(string examId, CancellationToken cancellationToken)
    {
        var examExists = await dbContext.Exams.AnyAsync(x => x.Id == examId && x.DeletedAt == null, cancellationToken);
        if (!examExists)
        {
            return NotFound();
        }

        var versions = await dbContext.ExamVersions
            .Where(x => x.ExamId == examId)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);

        return Ok(versions.Select(version => ToDto(version, [], [], [])).ToList());
    }

    [HttpGet("versions/{versionId}")]
    public async Task<ActionResult<ExamVersionDto>> GetVersion(string versionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blocks.Select(block => block.Id).Contains(x.ExamBlockId))
            .OrderBy(x => x.ExamBlockId)
            .ToListAsync(cancellationToken);
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        return Ok(ToDto(version, blocks, answerKeys, assets));
    }

    [HttpPut("versions/{versionId}/blocks")]
    public Task<ActionResult<BlockDto>> UpsertBlock(string versionId, UpsertBlockRequest request, CancellationToken cancellationToken)
    {
        return UpsertBlock(versionId, request.OrderIndex, request, cancellationToken);
    }

    [HttpPut("versions/{versionId}/blocks/{orderIndex:int}")]
    public async Task<ActionResult<BlockDto>> UpsertBlock(string versionId, int orderIndex, UpsertBlockRequest request, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (IsPublished(version))
        {
            return Conflict("Published exam versions are immutable. Create a new version before editing.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.ExamBlocks
            .SingleOrDefaultAsync(x => x.ExamVersionId == versionId && x.OrderIndex == orderIndex, cancellationToken);

        var block = new ExamBlock(
            existing?.Id ?? NewId(),
            versionId,
            orderIndex,
            request.BlockType,
            request.Title,
            request.Description,
            ToJsonDocument(request.Config),
            ToJsonDocument(request.Validation),
            existing?.CreatedAt ?? now,
            now);

        var validation = await blockValidator.ValidateAsync(block, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        if (existing is null)
        {
            dbContext.ExamBlocks.Add(block);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(block);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(block));
    }

    [HttpPost("versions/{versionId}/assets")]
    public async Task<ActionResult<AssetDto>> CreateAsset(string versionId, CreateAssetRequest request, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (IsPublished(version))
        {
            return Conflict("Published exam versions are immutable. Create a new version before editing.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName) ||
            string.IsNullOrWhiteSpace(request.MimeType) ||
            !request.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return BadRequest("fileName, image mimeType and contentBase64 are required.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return BadRequest("contentBase64 is not valid base64.");
        }

        var now = DateTimeOffset.UtcNow;
        var asset = new ExamAsset(
            NewId(),
            versionId,
            Path.GetFileName(request.FileName.Trim()),
            request.MimeType.Trim(),
            bytes.LongLength,
            HexSha256(bytes),
            $"base64:{request.ContentBase64}",
            now);

        dbContext.ExamAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetVersion), new { versionId }, ToDto(asset));
    }

    [HttpPost("versions/{versionId}/publish")]
    public async Task<ActionResult<PublishExamVersionResponse>> PublishVersion(string versionId, PublishExamVersionRequest request, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (IsPublished(version))
        {
            return Conflict("Exam version is already published.");
        }

        if (string.IsNullOrWhiteSpace(request.Grade))
        {
            ModelState.AddModelError(nameof(request.Grade), "Grade/course is required.");
            return ValidationProblem(ModelState);
        }

        var exam = await dbContext.Exams.SingleAsync(x => x.Id == version.ExamId, cancellationToken);
        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);

        if (blocks.Count == 0)
        {
            ModelState.AddModelError("blocks", "At least one block is required before publishing.");
            return ValidationProblem(ModelState);
        }

        foreach (var block in blocks)
        {
            var validation = await blockValidator.ValidateAsync(block, cancellationToken);
            if (!validation.IsValid)
            {
                return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
            }
        }

        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blocks.Select(block => block.Id).Contains(x.ExamBlockId))
            .ToListAsync(cancellationToken);
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        var missingAssetReferences = blocks
            .Where(static block => block.BlockType is PlanCope.Shared.Domain.BlockType.Image)
            .Select(block => block.Config.RootElement.TryGetProperty("assetId", out var assetId) ? assetId.GetString() : null)
            .Where(assetId => !string.IsNullOrWhiteSpace(assetId) && assets.All(asset => asset.Id != assetId))
            .ToList();

        if (missingAssetReferences.Count > 0)
        {
            ModelState.AddModelError("assets", "One or more image blocks reference assets that do not exist.");
            return ValidationProblem(ModelState);
        }

        var targets = BuildTargets(request, exam);
        var publishedAssets = assets.Select(ToPublishedDto).ToList();
        var checksumPayload = JsonSerializer.Serialize(new
        {
            ExamId = exam.Id,
            exam.Code,
            exam.Title,
            VersionId = version.Id,
            version.VersionNumber,
            version.SchemaVersion,
            Metadata = ToJsonElement(version.Metadata),
            Blocks = blocks.Select(ToDto),
            AnswerKeys = answerKeys.Select(ToDto),
            Assets = publishedAssets,
            Targets = targets
        });
        var checksum = HexSha256(Encoding.UTF8.GetBytes(checksumPayload));
        var now = DateTimeOffset.UtcNow;
        var package = new PublicationPackage(
            NewId(),
            versionId,
            1,
            checksum,
            ToJsonDocument(JsonSerializer.SerializeToElement(new
            {
                examId = exam.Id,
                examCode = exam.Code,
                title = exam.Title,
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                schemaVersion = version.SchemaVersion,
                targets
            })),
            "Published",
            now,
            now);

        dbContext.PublicationPackages.Add(package);
        dbContext.PublicationTargets.AddRange(targets.Select(target => new PublicationTarget(
            NewId(),
            package.Id,
            target.TargetType,
            target.TargetId,
            now,
            now)));

        var publishedVersion = version with { Status = "Published", PublishedAt = now, UpdatedAt = now };
        dbContext.Entry(version).CurrentValues.SetValues(publishedVersion);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PublishExamVersionResponse(package.Id, versionId, package.PackageVersion, checksum, targets));
    }

    [HttpPut("versions/{versionId}/document")]
    public async Task<ActionResult<ExamVersionDto>> ReplaceDocument(string versionId, ReplaceExamDocumentRequest request, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (IsPublished(version))
        {
            return Conflict("Published exam versions are immutable. Create a new version before editing.");
        }

        var now = DateTimeOffset.UtcNow;
        var newBlocks = new List<ExamBlock>();
        var newAnswerKeys = new List<AnswerKey>();

        var fallbackOrder = 0;
        foreach (var item in request.Blocks)
        {
            var block = new ExamBlock(
                NewId(),
                versionId,
                item.OrderIndex >= 0 ? item.OrderIndex : fallbackOrder,
                item.BlockType,
                item.Title,
                item.Description,
                ToJsonDocument(item.Config),
                ToJsonDocument(item.Validation),
                now,
                now);

            var validation = await blockValidator.ValidateAsync(block, cancellationToken);
            if (!validation.IsValid)
            {
                return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
            }

            newBlocks.Add(block);

            if (item.CorrectAnswer is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } correctAnswer)
            {
                newAnswerKeys.Add(new AnswerKey(
                    NewId(),
                    block.Id,
                    ToJsonDocument(correctAnswer),
                    item.ScoreValue,
                    null,
                    now,
                    now));
            }

            fallbackOrder++;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingBlocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .ToListAsync(cancellationToken);
        var existingBlockIds = existingBlocks.Select(x => x.Id).ToList();

        var existingAnswerKeys = await dbContext.AnswerKeys
            .Where(x => existingBlockIds.Contains(x.ExamBlockId))
            .ToListAsync(cancellationToken);
        var existingOptions = await dbContext.ExamBlockOptions
            .Where(x => existingBlockIds.Contains(x.ExamBlockId))
            .ToListAsync(cancellationToken);
        var existingUsages = await dbContext.AssetUsages
            .Where(x => existingBlockIds.Contains(x.ExamBlockId))
            .ToListAsync(cancellationToken);

        dbContext.AnswerKeys.RemoveRange(existingAnswerKeys);
        dbContext.ExamBlockOptions.RemoveRange(existingOptions);
        dbContext.AssetUsages.RemoveRange(existingUsages);
        dbContext.ExamBlocks.RemoveRange(existingBlocks);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.ExamBlocks.AddRange(newBlocks);
        dbContext.AnswerKeys.AddRange(newAnswerKeys);

        var metadata = request.Metadata.HasValue ? ToJsonDocument(request.Metadata.Value) : version.Metadata;
        var updatedVersion = version with { Metadata = metadata, UpdatedAt = now };
        dbContext.Entry(version).CurrentValues.SetValues(updatedVersion);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetVersion(versionId, cancellationToken);
    }

    private static ExamVersionDto ToDto(
        ExamVersion version,
        IReadOnlyList<ExamBlock> blocks,
        IReadOnlyList<AnswerKey> answerKeys,
        IReadOnlyList<ExamAsset> assets)
    {
        return new ExamVersionDto(
            version.Id,
            version.ExamId,
            version.VersionNumber,
            version.SchemaVersion,
            version.Status,
            ToJsonElement(version.Metadata),
            blocks.Select(ToDto).ToList(),
            answerKeys.Select(ToDto).ToList(),
            assets.Select(ToDto).ToList());
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
            ToJsonElement(block.Validation));
    }

    private static AnswerKeyDto ToDto(AnswerKey answerKey)
    {
        return new AnswerKeyDto(
            answerKey.Id,
            answerKey.ExamBlockId,
            answerKey.CorrectAnswer.RootElement.Clone(),
            answerKey.ScoreValue,
            ToJsonElement(answerKey.Metadata));
    }

    private static AssetDto ToDto(ExamAsset asset)
    {
        return new AssetDto(
            asset.Id,
            asset.ExamVersionId,
            asset.FileName,
            asset.MimeType,
            asset.SizeBytes,
            asset.Checksum,
            asset.StoragePath);
    }

    private static PublishedAssetDto ToPublishedDto(ExamAsset asset)
    {
        var contentBase64 = asset.StoragePath.StartsWith("base64:", StringComparison.Ordinal)
            ? asset.StoragePath["base64:".Length..]
            : string.Empty;

        return new PublishedAssetDto(
            asset.Id,
            asset.ExamVersionId,
            asset.FileName,
            asset.MimeType,
            asset.SizeBytes,
            asset.Checksum,
            contentBase64);
    }

    private static IReadOnlyList<PublicationTargetDto> BuildTargets(PublishExamVersionRequest request, Exam exam)
    {
        var targets = new List<PublicationTargetDto>
        {
            new("grade", request.Grade.Trim())
        };

        var subject = request.Subject ?? exam.Subject;
        if (!string.IsNullOrWhiteSpace(subject))
        {
            targets.Add(new PublicationTargetDto("subject", subject.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.Division))
        {
            targets.Add(new PublicationTargetDto("division", request.Division.Trim()));
        }

        return targets;
    }

    private static bool IsPublished(ExamVersion version)
    {
        return string.Equals(version.Status, "Published", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument ToJsonDocument(JsonElement value)
    {
        return JsonDocument.Parse(value.GetRawText());
    }

    private static JsonDocument? ToJsonDocument(JsonElement? value)
    {
        return value is null ? null : JsonDocument.Parse(value.Value.GetRawText());
    }

    private static JsonElement? ToJsonElement(JsonDocument? document)
    {
        return document?.RootElement.Clone();
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string HexSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}

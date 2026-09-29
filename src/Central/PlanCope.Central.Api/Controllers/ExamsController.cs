using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
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

        var summaries = await BuildExamSummariesAsync(exams, cancellationToken);
        return Ok(summaries);
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

        // Every new exam starts on a path to publication: an empty draft version (versionNumber 1)
        // whose id is returned so the author can add blocks and publish without a second call.
        var initialVersion = new ExamVersion(
            NewId(),
            exam.Id,
            1,
            1,
            "Draft",
            null,
            null,
            null,
            null,
            null,
            null,
            now,
            now,
            null);

        var versionValidation = await versionValidator.ValidateAsync(initialVersion, cancellationToken);
        if (!versionValidation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(versionValidation.ToDictionary()));
        }

        dbContext.Exams.Add(exam);
        dbContext.ExamVersions.Add(initialVersion);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new { id = exam.Id }, new ExamSummaryDto(
            exam.Id,
            exam.Code,
            exam.Title,
            exam.Level,
            exam.Area,
            exam.Subject,
            exam.Status,
            1,
            InitialVersionId: initialVersion.Id,
            PublicationState: ExamPublicationStates.Draft));
    }

    [HttpPost("{examId}/versions")]
    public async Task<ActionResult<ExamVersionDto>> CreateVersion(string examId, CreateExamVersionRequest request, CancellationToken cancellationToken)
    {
        var exam = await dbContext.Exams.SingleOrDefaultAsync(x => x.Id == examId && x.DeletedAt == null, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        var examVersions = await dbContext.ExamVersions
            .Where(x => x.ExamId == examId)
            .ToListAsync(cancellationToken);

        var latestVersionNumber = examVersions.Count == 0 ? 0 : examVersions.Max(static version => version.VersionNumber);

        // The new version is a deep copy of an existing version unless the caller asks for an empty
        // one. An explicit sourceVersionId wins; otherwise the latest version is the source. A draft
        // already existing for the exam is allowed (the web UI prevents it, the API does not).
        ExamVersion? source = null;
        if (!request.Empty)
        {
            if (!string.IsNullOrWhiteSpace(request.SourceVersionId))
            {
                source = examVersions.FirstOrDefault(version => string.Equals(version.Id, request.SourceVersionId, StringComparison.Ordinal));
                if (source is null)
                {
                    var belongsToAnotherExam = await dbContext.ExamVersions
                        .AnyAsync(version => version.Id == request.SourceVersionId, cancellationToken);
                    return belongsToAnotherExam
                        ? BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                        {
                            ["sourceVersionId"] = ["The source version does not belong to this exam."]
                        }))
                        : NotFound();
                }
            }
            else
            {
                source = examVersions
                    .OrderByDescending(static version => version.VersionNumber)
                    .FirstOrDefault();
            }
        }

        var now = DateTimeOffset.UtcNow;
        var version = new ExamVersion(
            NewId(),
            examId,
            latestVersionNumber + 1,
            request.SchemaVersion ?? source?.SchemaVersion ?? 1,
            "Draft",
            request.Metadata.HasValue ? ToJsonDocument(request.Metadata.Value) : Clone(source?.Metadata),
            null,
            null,
            null,
            null,
            null,
            now,
            now,
            request.ScoringPolicy ?? source?.ScoringPolicy,
            source?.Id);

        var validation = await versionValidator.ValidateAsync(version, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var newBlocks = new List<ExamBlock>();
        var newAnswerKeys = new List<AnswerKey>();
        var newOptions = new List<ExamBlockOption>();
        var newAssets = new List<ExamAsset>();
        var newUsages = new List<AssetUsage>();

        if (source is not null)
        {
            var sourceBlocks = await dbContext.ExamBlocks
                .Where(x => x.ExamVersionId == source.Id)
                .OrderBy(x => x.OrderIndex)
                .ToListAsync(cancellationToken);
            var sourceBlockIds = sourceBlocks.Select(static block => block.Id).ToList();

            var sourceAnswerKeys = await dbContext.AnswerKeys
                .Where(x => sourceBlockIds.Contains(x.ExamBlockId))
                .ToListAsync(cancellationToken);
            var sourceOptions = await dbContext.ExamBlockOptions
                .Where(x => sourceBlockIds.Contains(x.ExamBlockId))
                .ToListAsync(cancellationToken);
            var sourceAssets = await dbContext.ExamAssets
                .Where(x => x.ExamVersionId == source.Id)
                .ToListAsync(cancellationToken);
            var sourceUsages = await dbContext.AssetUsages
                .Where(x => sourceBlockIds.Contains(x.ExamBlockId))
                .ToListAsync(cancellationToken);

            // Assets are per-version rows: copy them under fresh ids and keep image blocks' config
            // pointing at the NEW asset ids so the copy owns its own files.
            var assetIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in sourceAssets)
            {
                var newAssetId = NewId();
                assetIdMap[asset.Id] = newAssetId;
                newAssets.Add(new ExamAsset(
                    newAssetId,
                    version.Id,
                    asset.FileName,
                    asset.MimeType,
                    asset.SizeBytes,
                    asset.Checksum,
                    asset.StoragePath,
                    now));
            }

            var blockIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var block in sourceBlocks)
            {
                var newBlockId = NewId();
                blockIdMap[block.Id] = newBlockId;
                newBlocks.Add(new ExamBlock(
                    newBlockId,
                    version.Id,
                    block.OrderIndex,
                    block.BlockType,
                    block.Title,
                    block.Description,
                    CloneConfigWithAssetRemap(block.Config, assetIdMap),
                    Clone(block.Validation),
                    now,
                    now));
            }

            foreach (var option in sourceOptions)
            {
                if (!blockIdMap.TryGetValue(option.ExamBlockId, out var newBlockId))
                {
                    continue;
                }

                newOptions.Add(new ExamBlockOption(
                    NewId(),
                    newBlockId,
                    option.Value,
                    option.Label,
                    option.OrderIndex,
                    Clone(option.Metadata),
                    now,
                    now));
            }

            foreach (var answerKey in sourceAnswerKeys)
            {
                if (!blockIdMap.TryGetValue(answerKey.ExamBlockId, out var newBlockId))
                {
                    continue;
                }

                newAnswerKeys.Add(new AnswerKey(
                    NewId(),
                    newBlockId,
                    Clone(answerKey.CorrectAnswer)!,
                    answerKey.ScoreValue,
                    Clone(answerKey.Metadata),
                    now,
                    now));
            }

            foreach (var usage in sourceUsages)
            {
                if (!blockIdMap.TryGetValue(usage.ExamBlockId, out var newBlockId) ||
                    !assetIdMap.TryGetValue(usage.ExamAssetId, out var newAssetId))
                {
                    continue;
                }

                newUsages.Add(new AssetUsage(NewId(), newBlockId, newAssetId, usage.UsageType, now, now));
            }
        }

        dbContext.ExamVersions.Add(version);
        dbContext.ExamBlocks.AddRange(newBlocks);
        dbContext.AnswerKeys.AddRange(newAnswerKeys);
        dbContext.ExamBlockOptions.AddRange(newOptions);
        dbContext.ExamAssets.AddRange(newAssets);
        dbContext.AssetUsages.AddRange(newUsages);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return Conflict("A version was created concurrently. Retry the request.");
        }

        var allVersions = examVersions.Append(version).ToList();
        return CreatedAtAction(nameof(GetVersion), new { versionId = version.Id }, ToDto(version, allVersions, newBlocks, newAnswerKeys, newAssets));
    }

    [HttpPut("{examId}")]
    public async Task<ActionResult<ExamSummaryDto>> UpdateExam(string examId, UpdateExamRequest request, CancellationToken cancellationToken)
    {
        var exam = await dbContext.Exams.SingleOrDefaultAsync(x => x.Id == examId && x.DeletedAt == null, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        // The code is the stable external identifier of the exam: it is immutable. Present and
        // different from the stored code is a validation error keyed "code".
        if (request.Code is not null && !string.Equals(request.Code.Trim(), exam.Code, StringComparison.Ordinal))
        {
            ModelState.AddModelError("code", "The exam code is immutable and cannot be changed.");
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        var updated = exam with
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Description = request.Description,
            Level = request.Level,
            Area = request.Area,
            Subject = request.Subject,
            UpdatedAt = now
        };

        var validation = await examValidator.ValidateAsync(updated, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        dbContext.Entry(exam).CurrentValues.SetValues(updated);
        await dbContext.SaveChangesAsync(cancellationToken);

        var summaries = await BuildExamSummariesAsync([updated], cancellationToken);
        return Ok(summaries[0]);
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

        var versionIds = versions.Select(static version => version.Id).ToList();
        var blocksByVersion = (await dbContext.ExamBlocks
                .Where(x => versionIds.Contains(x.ExamVersionId))
                .ToListAsync(cancellationToken))
            .GroupBy(static block => block.ExamVersionId)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ExamBlock>)group.ToList());

        return Ok(versions.Select(version =>
            ToDto(version, versions, blocksByVersion.GetValueOrDefault(version.Id) ?? [], [], [])).ToList());
    }

    [HttpGet("versions/{versionId}")]
    public async Task<ActionResult<ExamVersionDto>> GetVersion(string versionId, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        var examVersions = await dbContext.ExamVersions
            .Where(x => x.ExamId == version.ExamId)
            .ToListAsync(cancellationToken);
        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);
        // Hoist the block ids into a list: an inline `blocks.Select(...)` inside `Contains` is the
        // kind of projection EF Core can fail to translate against a real provider.
        var blockIds = blocks.Select(static block => block.Id).ToList();
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blockIds.Contains(x.ExamBlockId))
            .OrderBy(x => x.ExamBlockId)
            .ToListAsync(cancellationToken);
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        return Ok(ToDto(version, examVersions, blocks, answerKeys, assets));
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

        var hasMultipleChoiceBlock = blocks.Any(block => block.BlockType == PlanCope.Shared.Domain.BlockType.MultipleChoice);
        if (hasMultipleChoiceBlock && PlanCope.Shared.Grading.ScoringPolicyParser.Parse(version.ScoringPolicy) is null)
        {
            ModelState.AddModelError(
                "scoringPolicy",
                "A scoring policy must be chosen before publishing an exam with multiple-choice questions.");
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

        var blockIds = blocks.Select(static block => block.Id).ToList();
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blockIds.Contains(x.ExamBlockId))
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
        var checksum = ExamPackageChecksum.Compute(
            exam,
            version,
            blocks.Select(ToDto).ToList(),
            answerKeys.Select(ToDto).ToList(),
            publishedAssets,
            targets,
            ToJsonElement(version.Metadata));
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
                scoringPolicy = version.ScoringPolicy,
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
        var updatedVersion = version with { Metadata = metadata, UpdatedAt = now, ScoringPolicy = request.ScoringPolicy ?? version.ScoringPolicy };
        dbContext.Entry(version).CurrentValues.SetValues(updatedVersion);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetVersion(versionId, cancellationToken);
    }

    private static ExamVersionDto ToDto(
        ExamVersion version,
        IReadOnlyList<ExamVersion> examVersions,
        IReadOnlyList<ExamBlock> blocks,
        IReadOnlyList<AnswerKey> answerKeys,
        IReadOnlyList<ExamAsset> assets)
    {
        var (canPublish, publishBlockedReason) = EvaluatePublishReadiness(version, blocks);
        var (publishedAt, supersededAt, isCurrent, basedOnVersionNumber) = ComputePublication(version, examVersions);
        return new ExamVersionDto(
            version.Id,
            version.ExamId,
            version.VersionNumber,
            version.SchemaVersion,
            version.Status,
            ToJsonElement(version.Metadata),
            blocks.Select(ToDto).ToList(),
            answerKeys.Select(ToDto).ToList(),
            assets.Select(ToDto).ToList(),
            version.ScoringPolicy,
            blocks.Count,
            canPublish,
            publishBlockedReason,
            publishedAt,
            supersededAt,
            isCurrent,
            basedOnVersionNumber);
    }

    /// <summary>
    /// Computed publication fields for one version of an exam. The latest published version
    /// (highest <c>versionNumber</c>) is "current"; every other published version has been
    /// superseded by the next published version and reports that version's <c>publishedAt</c>.
    /// <c>basedOnVersionNumber</c> resolves the stored <see cref="ExamVersion.SourceVersionId"/>.
    /// </summary>
    private static (DateTimeOffset? PublishedAt, DateTimeOffset? SupersededAt, bool IsCurrent, int? BasedOnVersionNumber) ComputePublication(
        ExamVersion version,
        IReadOnlyList<ExamVersion> examVersions)
    {
        var published = examVersions
            .Where(IsPublished)
            .OrderBy(static candidate => candidate.VersionNumber)
            .ToList();

        var isCurrent = published.Count > 0 && string.Equals(published[^1].Id, version.Id, StringComparison.Ordinal);

        DateTimeOffset? supersededAt = null;
        if (!isCurrent && IsPublished(version))
        {
            supersededAt = published
                .FirstOrDefault(candidate => candidate.VersionNumber > version.VersionNumber)
                ?.PublishedAt;
        }

        int? basedOnVersionNumber = null;
        if (!string.IsNullOrWhiteSpace(version.SourceVersionId))
        {
            basedOnVersionNumber = examVersions
                .FirstOrDefault(candidate => string.Equals(candidate.Id, version.SourceVersionId, StringComparison.Ordinal))
                ?.VersionNumber;
        }

        return (version.PublishedAt, supersededAt, isCurrent, basedOnVersionNumber);
    }

    /// <summary>
    /// Per-version readiness independent of the publish request. The publish endpoint applies two
    /// additional request-level gates (<c>grade</c> required, block-level validation), so
    /// <see cref="ExamVersionDto.CanPublish"/> true means "this version is not blocked by its own
    /// content", not a guarantee the next publish call will succeed.
    /// </summary>
    private static (bool CanPublish, string? Reason) EvaluatePublishReadiness(ExamVersion version, IReadOnlyList<ExamBlock> blocks)
    {
        if (IsPublished(version))
        {
            return (false, "already_published");
        }

        if (blocks.Count == 0)
        {
            return (false, "no_blocks");
        }

        var hasMultipleChoiceBlock = blocks.Any(static block => block.BlockType == PlanCope.Shared.Domain.BlockType.MultipleChoice);
        if (hasMultipleChoiceBlock && PlanCope.Shared.Grading.ScoringPolicyParser.Parse(version.ScoringPolicy) is null)
        {
            return (false, "scoring_policy_required");
        }

        return (true, null);
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
        var targets = new List<PublicationTargetDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string targetType, string? targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return;
            }

            var trimmed = targetId.Trim();
            if (seen.Add($"{targetType}:{trimmed}"))
            {
                targets.Add(new PublicationTargetDto(targetType, trimmed));
            }
        }

        // grade/subject/division are descriptive metadata, not delivery filters.
        Add(PublicationTargetTypes.Grade, request.Grade);
        Add(PublicationTargetTypes.Subject, request.Subject ?? exam.Subject);
        Add(PublicationTargetTypes.Division, request.Division);

        // node/school targets opt a package into per-node delivery; absence of these means "all nodes".
        foreach (var nodeId in request.NodeIds ?? [])
        {
            Add(PublicationTargetTypes.Node, nodeId);
        }

        foreach (var schoolId in request.SchoolIds ?? [])
        {
            Add(PublicationTargetTypes.School, schoolId);
        }

        return targets;
    }

    private async Task<List<ExamSummaryDto>> BuildExamSummariesAsync(
        IReadOnlyList<Exam> exams,
        CancellationToken cancellationToken)
    {
        if (exams.Count == 0)
        {
            return [];
        }

        var examIds = exams.Select(static exam => exam.Id).ToList();
        var versions = await dbContext.ExamVersions
            .Where(x => examIds.Contains(x.ExamId))
            .ToListAsync(cancellationToken);
        var versionIds = versions.Select(static version => version.Id).ToList();

        var blockCounts = await dbContext.ExamBlocks
            .Where(x => versionIds.Contains(x.ExamVersionId))
            .GroupBy(static block => block.ExamVersionId)
            .Select(static group => new { VersionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(static row => row.VersionId, static row => row.Count, cancellationToken);

        var versionsByExam = versions
            .GroupBy(static version => version.ExamId)
            .ToDictionary(static group => group.Key, static group => group.ToList());
        var publishedVersionByExam = versions
            .Where(IsPublished)
            .GroupBy(static version => version.ExamId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(version => version.VersionNumber).First());

        var publishedVersionIds = publishedVersionByExam.Values.Select(static version => version.Id).ToList();
        var packagesByVersion = (await dbContext.PublicationPackages
                .Where(x => publishedVersionIds.Contains(x.ExamVersionId))
                .ToListAsync(cancellationToken))
            .GroupBy(static package => package.ExamVersionId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(package => package.PackageVersion).First());

        var packageIds = packagesByVersion.Values.Select(static package => package.Id).ToList();
        var targetsByPackage = (await dbContext.PublicationTargets
                .Where(x => packageIds.Contains(x.PublicationPackageId))
                .ToListAsync(cancellationToken))
            .GroupBy(static target => target.PublicationPackageId)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PublicationTargetDto>)group
                    .OrderBy(static target => target.TargetType)
                    .ThenBy(static target => target.TargetId)
                    .Select(static target => new PublicationTargetDto(target.TargetType, target.TargetId))
                    .ToList());

        var deliveryKeys = packageIds.Select(SyncCursorKeys.PackageDelivery).ToList();
        var deliveredByKey = (await dbContext.SyncCursors
                .Where(x => deliveryKeys.Contains(x.CursorKey))
                .Select(static cursor => cursor.CursorKey)
                .ToListAsync(cancellationToken))
            .GroupBy(static key => key)
            .ToDictionary(static group => group.Key, static group => group.Count());

        var result = new List<ExamSummaryDto>(exams.Count);
        foreach (var exam in exams)
        {
            var examVersions = versionsByExam.GetValueOrDefault(exam.Id) ?? [];
            var publicationState = ExamPublicationStates.Draft;
            string? publishedVersionId = null;
            int? publishedVersionNumber = null;
            DateTimeOffset? publishedAt = null;
            IReadOnlyList<PublicationTargetDto>? targets = null;
            int? pulledByNodeCount = null;

            if (publishedVersionByExam.TryGetValue(exam.Id, out var publishedVersion))
            {
                publicationState = ExamPublicationStates.Published;
                publishedVersionId = publishedVersion.Id;
                publishedVersionNumber = publishedVersion.VersionNumber;
                publishedAt = publishedVersion.PublishedAt;
                targets = [];

                if (packagesByVersion.TryGetValue(publishedVersion.Id, out var package))
                {
                    targets = targetsByPackage.GetValueOrDefault(package.Id) ?? [];
                    pulledByNodeCount = deliveredByKey.GetValueOrDefault(SyncCursorKeys.PackageDelivery(package.Id));
                }
            }
            else if (examVersions.Any(version => blockCounts.GetValueOrDefault(version.Id) > 0))
            {
                publicationState = ExamPublicationStates.ReadyToPublish;
            }

            result.Add(new ExamSummaryDto(
                exam.Id,
                exam.Code,
                exam.Title,
                exam.Level,
                exam.Area,
                exam.Subject,
                exam.Status,
                examVersions.Count,
                PublicationState: publicationState,
                PublishedVersionId: publishedVersionId,
                PublishedVersionNumber: publishedVersionNumber,
                PublishedAt: publishedAt,
                Targets: targets,
                PulledByNodeCount: pulledByNodeCount));
        }

        return result;
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

    /// <summary>Deep-copies a JSON document so the copy never shares mutable state with the source.</summary>
    private static JsonDocument? Clone(JsonDocument? document)
    {
        return document is null ? null : JsonDocument.Parse(document.RootElement.GetRawText());
    }

    /// <summary>
    /// Deep-copies a block config, rewriting every string value that matches a copied asset id to
    /// the new asset id. Image blocks keep their <c>config.assetId</c> valid on the copied version.
    /// </summary>
    private static JsonDocument CloneConfigWithAssetRemap(JsonDocument config, IReadOnlyDictionary<string, string> assetIdMap)
    {
        var node = JsonNode.Parse(config.RootElement.GetRawText());
        if (node is not null)
        {
            RemapAssetIds(node, assetIdMap);
        }

        return JsonDocument.Parse(node?.ToJsonString() ?? "{}");
    }

    private static void RemapAssetIds(JsonNode node, IReadOnlyDictionary<string, string> assetIdMap)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (property.Value is JsonValue value &&
                        value.TryGetValue<string>(out var text) &&
                        text is not null &&
                        assetIdMap.TryGetValue(text, out var replacement))
                    {
                        jsonObject[property.Key] = replacement;
                    }
                    else if (property.Value is not null)
                    {
                        RemapAssetIds(property.Value, assetIdMap);
                    }
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null)
                    {
                        RemapAssetIds(item, assetIdMap);
                    }
                }

                break;
        }
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

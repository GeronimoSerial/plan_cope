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

    [HttpPut("versions/{versionId}/blocks/{orderIndex:int}")]
    public async Task<ActionResult<BlockDto>> UpsertBlock(string versionId, int orderIndex, UpsertBlockRequest request, CancellationToken cancellationToken)
    {
        var versionExists = await dbContext.ExamVersions.AnyAsync(x => x.Id == versionId, cancellationToken);
        if (!versionExists)
        {
            return NotFound();
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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

public sealed class LocalExamImportService(ILocalExamRepository repository, LocalAssetFileService assetFileService)
{
    public async Task<ImportLocalExamResult> ImportAsync(ImportLocalExamRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation.Count > 0)
        {
            return ImportLocalExamResult.Failure(validation);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var versionNumber = request.VersionNumber.GetValueOrDefault(1);
        var examId = CleanId(request.Id) ?? DeterministicId("manual-exam", request.ExamCode, versionNumber.ToString());
        var metadataJson = JsonSerializer.Serialize(new
        {
            title = request.Title.Trim(),
            grade = request.Grade?.Trim(),
            division = request.Division?.Trim(),
            subject = request.Subject?.Trim(),
            source = "manual-import"
        });

        var assets = await ImportAssetsAsync(examId, request.Assets ?? [], now, cancellationToken);
        var blocks = BuildBlocks(examId, request.Blocks!);
        var answerKeys = BuildAnswerKeys(examId, request.Blocks!);
        var exam = BuildExam(request, examId, versionNumber, metadataJson, blocks, assets, now);

        await repository.UpsertImportedExamAsync(exam, blocks, assets, answerKeys, cancellationToken);

        return ImportLocalExamResult.Success(new ImportLocalExamResponse(
            exam.Id,
            exam.ExamCode,
            exam.VersionNumber,
            blocks.Count,
            assets.Count));
    }

    private async Task<IReadOnlyList<LocalAsset>> ImportAssetsAsync(
        string examId,
        IReadOnlyList<ImportLocalExamAsset> assets,
        string importedAt,
        CancellationToken cancellationToken)
    {
        var importedAssets = new List<LocalAsset>(assets.Count);

        foreach (var asset in assets)
        {
            var assetId = CleanId(asset.Id) ?? DeterministicId("manual-asset", examId, asset.FileName);
            var fileName = Path.GetFileName(asset.FileName.Trim());
            var bytes = await ReadAssetBytesAsync(asset, cancellationToken);
            var savedAsset = await assetFileService.SaveAsync(assetId, fileName, asset.MimeType.Trim(), bytes, cancellationToken);

            importedAssets.Add(new LocalAsset(
                assetId,
                $"manual:{assetId}",
                examId,
                fileName,
                savedAsset.MimeType,
                savedAsset.Checksum,
                savedAsset.Path,
                importedAt));
        }

        return importedAssets;
    }

    private static IReadOnlyList<LocalExamBlock> BuildBlocks(string examId, IReadOnlyList<ImportLocalExamBlock> blocks)
    {
        return blocks.Select((block, index) =>
        {
            var blockId = CleanId(block.Id) ?? $"{examId}-block-{index + 1:000}";
            return new LocalExamBlock(
                blockId,
                examId,
                $"manual:{blockId}",
                index,
                ParseBlockType(block.Type),
                block.Config.GetRawText(),
                block.Validation?.GetRawText());
        }).ToList();
    }

    private static IReadOnlyList<LocalAnswerKey> BuildAnswerKeys(string examId, IReadOnlyList<ImportLocalExamBlock> blocks)
    {
        return blocks
            .Select((block, index) => new { Block = block, BlockId = CleanId(block.Id) ?? $"{examId}-block-{index + 1:000}" })
            .Where(item => item.Block.AnswerKey is not null)
            .Select(item => new LocalAnswerKey(
                $"{item.BlockId}-answer-key",
                examId,
                $"manual:{item.BlockId}",
                item.Block.AnswerKey!.CorrectAnswer.GetRawText(),
                item.Block.AnswerKey.ScoreValue))
            .ToList();
    }

    private static LocalExamVersion BuildExam(
        ImportLocalExamRequest request,
        string examId,
        int versionNumber,
        string metadataJson,
        IReadOnlyList<LocalExamBlock> blocks,
        IReadOnlyList<LocalAsset> assets,
        string importedAt)
    {
        var checksumInput = JsonSerializer.Serialize(new
        {
            request.ExamCode,
            versionNumber,
            metadataJson,
            blocks = blocks.Select(static block => new { block.Id, block.BlockType, block.ConfigJson, block.ValidationJson }),
            assets = assets.Select(static asset => new { asset.Id, asset.Checksum })
        });

        return new LocalExamVersion(
            examId,
            $"manual:{examId}",
            request.ExamCode.Trim(),
            versionNumber,
            HexSha256(Encoding.UTF8.GetBytes(checksumInput)),
            metadataJson,
            1,
            importedAt);
    }

    private static List<string> Validate(ImportLocalExamRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ExamCode))
        {
            errors.Add("examCode is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("title is required.");
        }

        if (request.VersionNumber is <= 0)
        {
            errors.Add("versionNumber must be greater than zero.");
        }

        if (request.Blocks is null || request.Blocks.Count == 0)
        {
            errors.Add("blocks must contain at least one block.");
            return errors;
        }

        var assetIds = ValidateAssets(request.Assets ?? [], errors);
        ValidateBlocks(request.Blocks, assetIds, errors);
        return errors;
    }

    private static HashSet<string> ValidateAssets(IReadOnlyList<ImportLocalExamAsset> assets, List<string> errors)
    {
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            var assetId = CleanId(asset.Id);
            if (assetId is null)
            {
                errors.Add($"assets[{i}].id is required.");
            }
            else if (!assetIds.Add(assetId))
            {
                errors.Add($"assets[{i}].id must be unique.");
            }

            if (string.IsNullOrWhiteSpace(asset.FileName))
            {
                errors.Add($"assets[{i}].fileName is required.");
            }

            if (string.IsNullOrWhiteSpace(asset.MimeType) || !asset.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"assets[{i}].mimeType must be an image MIME type.");
            }

            if (string.IsNullOrWhiteSpace(asset.ContentBase64))
            {
                errors.Add($"assets[{i}].contentBase64 is required.");
            }
            else if (!IsValidBase64(asset.ContentBase64))
            {
                errors.Add($"assets[{i}].contentBase64 is not valid base64.");
            }
        }

        return assetIds;
    }

    private static void ValidateBlocks(IReadOnlyList<ImportLocalExamBlock> blocks, HashSet<string> assetIds, List<string> errors)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (!TryParseBlockType(block.Type, out var blockType))
            {
                errors.Add($"blocks[{i}].type is not supported.");
                continue;
            }

            if (block.Config.ValueKind is not JsonValueKind.Object)
            {
                errors.Add($"blocks[{i}].config must be an object.");
                continue;
            }

            ValidateBlockConfig(block.Config, blockType, i, assetIds, errors);
            ValidateBlockRules(block.Validation, i, errors);
        }
    }

    private static void ValidateBlockRules(JsonElement? validation, int index, List<string> errors)
    {
        if (validation is { ValueKind: not JsonValueKind.Object })
        {
            errors.Add($"blocks[{index}].validation must be an object when present.");
            return;
        }

        if (validation is { } rules &&
            rules.TryGetProperty("required", out var required) &&
            required.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            errors.Add($"blocks[{index}].validation.required must be a boolean when present.");
        }
    }

    private static void ValidateBlockConfig(JsonElement config, BlockType blockType, int index, HashSet<string> assetIds, List<string> errors)
    {
        if (blockType is BlockType.Text && !HasString(config, "content"))
        {
            errors.Add($"blocks[{index}].config.content is required.");
        }

        if (blockType is BlockType.MultipleChoice)
        {
            ValidateMultipleChoice(config, index, errors);
        }

        if (blockType is BlockType.TrueFalse && !HasString(config, "question"))
        {
            errors.Add($"blocks[{index}].config.question is required.");
        }

        if (blockType is BlockType.ShortAnswer && !HasString(config, "prompt"))
        {
            errors.Add($"blocks[{index}].config.prompt is required.");
        }

        if (blockType is BlockType.Image)
        {
            ValidateImage(config, index, assetIds, errors);
        }
    }

    private static void ValidateMultipleChoice(JsonElement config, int index, List<string> errors)
    {
        if (!HasString(config, "question"))
        {
            errors.Add($"blocks[{index}].config.question is required.");
        }

        if (!config.TryGetProperty("options", out var options) || options.ValueKind is not JsonValueKind.Array || options.GetArrayLength() < 2)
        {
            errors.Add($"blocks[{index}].config.options requires at least two options.");
            return;
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var optionIndex = 0;
        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind is not JsonValueKind.Object || !HasString(option, "value") || !HasString(option, "label"))
            {
                errors.Add($"blocks[{index}].config.options[{optionIndex}] requires value and label.");
            }
            else if (!values.Add(option.GetProperty("value").GetString()!))
            {
                errors.Add($"blocks[{index}].config.options values must be unique.");
            }

            optionIndex++;
        }
    }

    private static void ValidateImage(JsonElement config, int index, HashSet<string> assetIds, List<string> errors)
    {
        if (!HasString(config, "assetId"))
        {
            errors.Add($"blocks[{index}].config.assetId is required.");
            return;
        }

        if (!assetIds.Contains(config.GetProperty("assetId").GetString()!))
        {
            errors.Add($"blocks[{index}].config.assetId must reference an imported asset.");
        }
    }

    private static async Task<byte[]> ReadAssetBytesAsync(ImportLocalExamAsset asset, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.FromBase64String(asset.ContentBase64!);
    }

    private static bool HasString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString());
    }

    private static BlockType ParseBlockType(string type)
    {
        if (TryParseBlockType(type, out var blockType))
        {
            return blockType;
        }

        throw new InvalidOperationException($"Unsupported block type '{type}'.");
    }

    private static bool TryParseBlockType(string type, out BlockType blockType)
    {
        blockType = type.Trim().ToLowerInvariant() switch
        {
            "text" => BlockType.Text,
            "image" => BlockType.Image,
            "multiple_choice" or "multiplechoice" => BlockType.MultipleChoice,
            "true_false" or "truefalse" => BlockType.TrueFalse,
            "short_answer" or "shortanswer" => BlockType.ShortAnswer,
            _ => default
        };

        return type.Trim().ToLowerInvariant() is "text" or "image" or "multiple_choice" or "multiplechoice" or "true_false" or "truefalse" or "short_answer" or "shortanswer";
    }

    private static bool IsValidBase64(string value)
    {
        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string HexSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DeterministicId(params string[] parts)
    {
        var input = string.Join(":", parts.Select(static part => part.Trim().ToLowerInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..24].ToLowerInvariant();
    }

    private static string? CleanId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Trim().Select(static c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}

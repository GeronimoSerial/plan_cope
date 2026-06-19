using System.Net.Http.Headers;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

public sealed class LocalExamPullService(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    ILocalExamRepository examRepository,
    LocalAssetFileService assetFileService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LocalExamPullResult> PullAsync(CancellationToken cancellationToken)
    {
        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var nodeId = await ReadStateStringAsync("node_id", cancellationToken);
        var cursor = await ReadStateStringAsync("last_exam_pull_cursor", cancellationToken) ?? "0";
        var token = await ReadStateStringAsync("central_access_token", cancellationToken);

        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
        {
            return new LocalExamPullResult(false, 0, cursor, "central_url and node_id must be configured in sync_state.");
        }

        var client = httpClientFactory.CreateClient(nameof(LocalExamPullService));
        client.BaseAddress = new Uri(centralUrl.Trim().TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.GetAsync($"api/sync/pull?nodeId={Uri.EscapeDataString(nodeId)}&cursor={Uri.EscapeDataString(cursor)}&limit=50", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return new LocalExamPullResult(false, 0, cursor, $"Central pull failed: {(int)response.StatusCode} {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var pull = await JsonSerializer.DeserializeAsync<PullResponse>(stream, JsonOptions, cancellationToken);
        if (pull is null)
        {
            return new LocalExamPullResult(false, 0, cursor, "Central pull returned an empty response.");
        }

        var imported = 0;
        foreach (var item in pull.Items.Where(static item => item.EntityType == "publication_package" && item.Operation == "upsert"))
        {
            var package = item.Payload.Deserialize<PublishedExamPackageDto>(JsonOptions);
            if (package is null)
            {
                continue;
            }

            if (!string.Equals(package.Checksum, item.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                return new LocalExamPullResult(false, imported, cursor, $"Checksum mismatch for package {item.EntityId}.");
            }

            await ImportPackageAsync(package, cancellationToken);
            imported++;
        }

        await UpsertStateStringAsync("last_exam_pull_cursor", pull.NextCursor, cancellationToken);
        return new LocalExamPullResult(true, imported, pull.NextCursor, null);
    }

    private async Task ImportPackageAsync(PublishedExamPackageDto package, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var localExamId = package.ExamVersionId;
        var metadataJson = JsonSerializer.Serialize(new
        {
            title = package.Title,
            grade = TargetValue(package, "grade"),
            division = TargetValue(package, "division"),
            subject = TargetValue(package, "subject"),
            source = "central-pull",
            packageId = package.PackageId,
            remoteExamId = package.ExamId
        }, JsonOptions);

        var assets = new List<LocalAsset>(package.Assets.Count);
        foreach (var asset in package.Assets)
        {
            var bytes = Convert.FromBase64String(asset.ContentBase64);
            var saved = await assetFileService.SaveAsync(asset.Id, asset.FileName, asset.MimeType, bytes, cancellationToken);
            assets.Add(new LocalAsset(
                asset.Id,
                asset.Id,
                localExamId,
                asset.FileName,
                saved.MimeType,
                saved.Checksum,
                saved.Path,
                now));
        }

        var blocks = package.Blocks
            .OrderBy(static block => block.OrderIndex)
            .Select(block => new LocalExamBlock(
                block.Id,
                localExamId,
                block.Id,
                block.OrderIndex,
                block.BlockType,
                block.Config.GetRawText(),
                block.Validation?.GetRawText()))
            .ToList();

        var answerKeys = package.AnswerKeys
            .Select(answerKey => new LocalAnswerKey(
                answerKey.Id,
                localExamId,
                answerKey.BlockId,
                answerKey.CorrectAnswer.GetRawText(),
                answerKey.ScoreValue is null ? null : Convert.ToDouble(answerKey.ScoreValue.Value)))
            .ToList();

        var exam = new LocalExamVersion(
            localExamId,
            package.ExamVersionId,
            package.ExamCode,
            package.VersionNumber,
            package.Checksum,
            metadataJson,
            package.SchemaVersion,
            now);

        await examRepository.UpsertImportedExamAsync(exam, blocks, assets, answerKeys, cancellationToken);
    }

    private static string? TargetValue(PublishedExamPackageDto package, string targetType)
    {
        return package.Targets.FirstOrDefault(target => string.Equals(target.TargetType, targetType, StringComparison.OrdinalIgnoreCase))?.TargetId;
    }

    private async Task<string?> ReadStateStringAsync(string key, CancellationToken cancellationToken)
    {
        var state = await syncStateRepository.GetAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(state?.ValueJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(state.ValueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }

    private Task UpsertStateStringAsync(string key, string value, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            JsonSerializer.Serialize(value, JsonOptions),
            now), cancellationToken);
    }
}

public sealed record LocalExamPullResult(bool Success, int Imported, string Cursor, string? Error);

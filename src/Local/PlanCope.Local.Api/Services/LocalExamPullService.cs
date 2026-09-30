using System.Net;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

/// <summary>
/// Pulls published exam packages from Central and upserts them locally. Used both by the
/// autonomous <see cref="SyncBackgroundService"/> (every idle tick) and by the operator-facing
/// on-demand endpoint, so a single implementation must page until <c>hasMore</c> is false and
/// must classify failures into stable <see cref="ExamPullErrorCodes"/> values.
/// </summary>
public interface ILocalExamPullService
{
    Task<LocalExamPullResult> PullAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalExamPullService(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    ILocalExamRepository examRepository,
    LocalAssetFileService assetFileService,
    ExamPullGate? pullGate = null) : ILocalExamPullService
{
    // 50 matches the Central default; the hard page cap keeps a pathological Central from
    // spinning the endpoint forever (50 pages * 50 = 2500 packages per invocation).
    private const int PageSize = 50;
    private const int MaxPagesPerPull = 50;

    private const string CentralUrlKey = "central_url";
    private const string NodeIdKey = "node_id";
    private const string CursorKey = "last_exam_pull_cursor";
    private const string LastPullAtKey = "last_pull_at";
    private const string LastErrorKey = "sync_last_error";
    private const string OfflineKey = "sync_offline";

    // Direct constructions (unit tests) that omit the gate get their own isolated one; the
    // production DI graph always injects the shared singleton.
    private static readonly ExamPullGate FallbackGate = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LocalExamPullResult> PullAsync(CancellationToken cancellationToken)
    {
        var gate = pullGate ?? FallbackGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await PullCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LocalExamPullResult> PullCoreAsync(CancellationToken cancellationToken)
    {
        var lastPullAt = await ReadLastPullAtAsync(cancellationToken);
        var centralUrl = await ReadStateStringAsync(CentralUrlKey, cancellationToken);
        var nodeId = await ReadStateStringAsync(NodeIdKey, cancellationToken);
        var cursor = await ReadStateStringAsync(CursorKey, cancellationToken) ?? "0";

        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
        {
            return await FailAsync(ExamPullErrorCodes.NotEnrolled, cursor, lastPullAt, cancellationToken);
        }

        if (!Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("http" or "https"))
        {
            return await FailAsync(ExamPullErrorCodes.CentralUnreachable, cursor, lastPullAt, cancellationToken);
        }

        var client = httpClientFactory.CreateClient(nameof(LocalExamPullService));
        client.BaseAddress = baseAddress;

        var newExams = 0;
        var updatedExams = 0;
        var totalReceived = 0;

        try
        {
            for (var page = 0; page < MaxPagesPerPull; page++)
            {
                using var response = await client.GetAsync(
                    $"api/sync/pull?nodeId={Uri.EscapeDataString(nodeId)}&cursor={Uri.EscapeDataString(cursor)}&limit={PageSize}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorCode = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? ExamPullErrorCodes.Unauthorized
                        : ExamPullErrorCodes.CentralUnreachable;
                    return await FailAsync(errorCode, cursor, lastPullAt, cancellationToken);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var pull = await JsonSerializer.DeserializeAsync<PullResponse>(stream, JsonOptions, cancellationToken);
                if (pull is null)
                {
                    return await FailAsync(ExamPullErrorCodes.Unknown, cursor, lastPullAt, cancellationToken);
                }

                foreach (var item in pull.Items)
                {
                    if (!string.Equals(item.EntityType, "publication_package", StringComparison.Ordinal) ||
                        !string.Equals(item.Operation, "upsert", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    totalReceived++;
                    var package = item.Payload.Deserialize<PublishedExamPackageDto>(JsonOptions);
                    if (package is null)
                    {
                        continue;
                    }

                    if (!string.Equals(package.Checksum, item.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        return await FailAsync(ExamPullErrorCodes.ChecksumMismatch, cursor, lastPullAt, cancellationToken);
                    }

                    // A brand-new version of an exam that is already known locally (same exam_code)
                    // is an UPDATE of that exam, not a new exam: the catalog still lists one entry.
                    // Only a package whose exam_code is unseen counts as a new exam.
                    var existing = await examRepository.GetByIdAsync(package.ExamVersionId, cancellationToken);
                    if (existing is null)
                    {
                        if (await examRepository.ExistsByExamCodeAsync(package.ExamCode, cancellationToken))
                        {
                            updatedExams++;
                        }
                        else
                        {
                            newExams++;
                        }
                    }
                    else if (!string.Equals(existing.Checksum, package.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        updatedExams++;
                    }

                    await ImportPackageAsync(package, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(pull.NextCursor))
                {
                    cursor = pull.NextCursor;
                }

                if (!pull.HasMore)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return await FailAsync(ExamPullErrorCodes.CentralUnreachable, cursor, lastPullAt, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            // Malformed package payload: a Central data problem, not a transport one.
            return await FailAsync(ExamPullErrorCodes.Unknown, cursor, lastPullAt, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        await UpsertStateStringAsync(CursorKey, cursor, cancellationToken);
        await UpsertStateStringAsync(LastPullAtKey, now.ToString("O"), cancellationToken);
        // Mirror the background service: a successful pull clears the operator-visible error and
        // proves Central is reachable again.
        await UpsertStateStringAsync(LastErrorKey, string.Empty, cancellationToken);
        await UpsertStateValueAsync(OfflineKey, JsonSerializer.Serialize(false, JsonOptions), cancellationToken);

        return new LocalExamPullResult(
            true,
            newExams + updatedExams,
            cursor,
            null,
            newExams,
            updatedExams,
            totalReceived,
            null,
            now);
    }

    private async Task<LocalExamPullResult> FailAsync(
        string errorCode,
        string cursor,
        DateTimeOffset? lastPullAt,
        CancellationToken cancellationToken)
    {
        var message = ExamPullMessages.ForError(errorCode);
        await UpsertStateStringAsync(LastErrorKey, message, cancellationToken);
        if (errorCode == ExamPullErrorCodes.CentralUnreachable)
        {
            await UpsertStateValueAsync(OfflineKey, JsonSerializer.Serialize(true, JsonOptions), cancellationToken);
        }

        return new LocalExamPullResult(false, 0, cursor, message, 0, 0, 0, errorCode, lastPullAt);
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
            now,
            package.ScoringPolicy);

        await examRepository.UpsertImportedExamAsync(exam, blocks, assets, answerKeys, cancellationToken);
    }

    private static string? TargetValue(PublishedExamPackageDto package, string targetType)
    {
        return package.Targets.FirstOrDefault(target => string.Equals(target.TargetType, targetType, StringComparison.OrdinalIgnoreCase))?.TargetId;
    }

    private async Task<DateTimeOffset?> ReadLastPullAtAsync(CancellationToken cancellationToken)
    {
        var value = await ReadStateStringAsync(LastPullAtKey, cancellationToken);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
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
        return UpsertStateValueAsync(key, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
    }

    private Task UpsertStateValueAsync(string key, string valueJson, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            valueJson,
            now), cancellationToken);
    }
}

public static class ExamPullErrorCodes
{
    public const string NotEnrolled = "not_enrolled";
    public const string CentralUnreachable = "central_unreachable";
    public const string Unauthorized = "unauthorized";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string Unknown = "unknown";
}

/// <summary>
/// Short neutral Spanish sentences shown verbatim by the operator UI; kept next to the error
/// codes so the two can never drift.
/// </summary>
public static class ExamPullMessages
{
    public static string ForError(string? errorCode) => errorCode switch
    {
        ExamPullErrorCodes.NotEnrolled => "Este equipo todavía no está vinculado con Central.",
        ExamPullErrorCodes.CentralUnreachable => "No se pudo conectar con Central.",
        ExamPullErrorCodes.Unauthorized => "Central rechazó la credencial del equipo. Volvé a inscribirlo.",
        ExamPullErrorCodes.ChecksumMismatch => "Un examen recibido no superó la verificación de integridad.",
        _ => "No se pudieron buscar exámenes nuevos."
    };

    public static string ForSuccess(int newExams, int updatedExams)
    {
        if (newExams > 0 && updatedExams > 0)
        {
            return $"Se importaron {newExams} exámenes nuevos y se actualizaron {updatedExams}.";
        }

        if (newExams > 0)
        {
            return newExams == 1 ? "Se importó 1 examen nuevo." : $"Se importaron {newExams} exámenes nuevos.";
        }

        if (updatedExams > 0)
        {
            return updatedExams == 1 ? "Se actualizó 1 examen." : $"Se actualizaron {updatedExams} exámenes.";
        }

        return "No hay exámenes nuevos.";
    }
}

public sealed record LocalExamPullResult(
    bool Success,
    int Imported,
    string Cursor,
    string? Error,
    int NewExams = 0,
    int UpdatedExams = 0,
    int TotalReceived = 0,
    string? ErrorCode = null,
    DateTimeOffset? LastPullAt = null)
{
    public PullExamsResponse ToResponse()
    {
        var status = !Success
            ? "error"
            : NewExams + UpdatedExams > 0
                ? "updated"
                : "up_to_date";
        var message = Success
            ? ExamPullMessages.ForSuccess(NewExams, UpdatedExams)
            : ExamPullMessages.ForError(ErrorCode);

        return new PullExamsResponse(
            status,
            NewExams,
            UpdatedExams,
            TotalReceived,
            ErrorCode,
            message,
            LastPullAt?.ToString("O"));
    }
}

public sealed record PullExamsResponse(
    string Status,
    int NewExams,
    int UpdatedExams,
    int TotalReceived,
    string? ErrorCode,
    string Message,
    string? LastPullAt);

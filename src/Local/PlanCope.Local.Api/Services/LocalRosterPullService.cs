using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Local.Api.Services;

/// <summary>
/// Performs the explicitly requested roster pull. This service is intentionally
/// not a hosted service and has no timer or scheduler: a release/operator calls
/// the endpoint when the node should receive a new snapshot.
/// </summary>
public interface ILocalRosterPullService
{
    Task<LocalRosterBulkPullResult> PullAllAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalRosterPullService(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    ILocalRosterRepository rosterRepository,
    IDocumentHmacService documentHmacService,
    ILogger<LocalRosterPullService> logger) : ILocalRosterPullService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LocalRosterPullResult> PullAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        if (!CueCode.TryNormalize(cue, out cue))
        {
            return Failure($"cue must contain exactly {CueCode.Length} digits.");
        }
        schoolYear = schoolYear?.Trim() ?? string.Empty;
        if (schoolYear.Length == 0 || schoolYear.Length > GeRosterTransportLimits.MaxSchoolYearLength)
        {
            return Failure("cue and schoolYear are required and must be within the supported limits.");
        }

        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var nodeId = await ReadStateStringAsync("node_id", cancellationToken);
        var token = await ReadStateStringAsync("central_access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
        {
            return Failure("central_url and node_id must be configured in sync_state.");
        }

        if (!Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("http" or "https"))
        {
            return Failure("central_url is invalid.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(LocalRosterPullService));
            client.BaseAddress = baseAddress;
            client.DefaultRequestHeaders.Remove("X-Node-Id");
            client.DefaultRequestHeaders.Add("X-Node-Id", nodeId);

            var route = $"api/sync/roster/{Uri.EscapeDataString(cue)}/{Uri.EscapeDataString(schoolYear)}";
            using var response = await client.GetAsync(route, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Central roster pull returned HTTP {StatusCode} for CUE {Cue} and school year {SchoolYear}: {ResponseBody}",
                    (int)response.StatusCode, cue, schoolYear, error);
                return Failure($"Central roster pull failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var package = await JsonSerializer.DeserializeAsync<GeRosterPackageDto>(stream, JsonOptions, cancellationToken);
            if (package is null)
            {
                return Failure("Central roster pull returned an empty response.");
            }

            // Validate the checksum and all shape/size constraints before opening
            // SQLite or asking the HMAC service to process a document.
            LocalRosterPackageValidator.Validate(package);
            if (!string.Equals(package.Cue, cue, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(package.SchoolYear, schoolYear, StringComparison.Ordinal))
            {
                return Failure("Central returned a roster for a different CUE or school year.");
            }

            var result = await rosterRepository.ImportAsync(package, documentHmacService, cancellationToken);
            await syncStateRepository.UpsertAsync(new SyncState(
                Guid.NewGuid().ToString("N"),
                "last_roster_pull_at",
                JsonSerializer.Serialize(DateTimeOffset.UtcNow, JsonOptions),
                DateTimeOffset.UtcNow.ToString("O")), cancellationToken);

            return new LocalRosterPullResult(
                true,
                result.Imported,
                result.SnapshotId,
                result.Checksum,
                result.SectionCount,
                result.StudentCount,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Roster pull failed for CUE {Cue} and school year {SchoolYear}.", cue, schoolYear);
            return Failure(exception.Message);
        }
    }

    public async Task<LocalRosterBulkPullResult> PullAllAsync(CancellationToken cancellationToken = default)
    {
        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var nodeId = await ReadStateStringAsync("node_id", cancellationToken);
        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
            return new(false, 0, 0, 0, "Central credentials are not configured.");
        if (!Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress))
            return new(false, 0, 0, 0, "Central URL is invalid.");

        try
        {
            var client = httpClientFactory.CreateClient(nameof(LocalRosterPullService));
            client.BaseAddress = baseAddress;
            using var response = await client.GetAsync("api/sync/rosters/index", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, 0, 0, 0, $"Central roster index failed: {(int)response.StatusCode}.");
            var index = await response.Content.ReadFromJsonAsync<RosterIndex>(JsonOptions, cancellationToken);
            if (index is null)
                return new(false, 0, 0, 0, "Central returned an empty roster index.");

            var skipped = await rosterRepository.UpsertSchoolsAsync((index.Schools ?? [])
                .Select(static school => new LocalSchoolSummary(school.Cue, school.Name)).ToArray(), cancellationToken);
            var rosters = index.Rosters ?? [];
            var total = rosters.Count;
            var imported = 0;
            var completed = 0;
            var failed = 0;
            var skippedRosters = 0;
            var errors = new ConcurrentQueue<RosterPullFailure>();
            using var progressLock = new SemaphoreSlim(1, 1);
            await WriteProgressAsync("rosters", 0, total, skipped, cancellationToken);
            await Parallel.ForEachAsync(rosters, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4
            }, async (roster, ct) =>
            {
                if (!CueCode.TryNormalize(roster.Cue, out _))
                {
                    Interlocked.Increment(ref skippedRosters);
                    logger.LogWarning("Skipping Central roster index entry with invalid CUE {Cue} for school year {SchoolYear}.", roster.Cue, roster.SchoolYear);
                }
                else
                {
                    var result = await PullAsync(roster.Cue, roster.SchoolYear, ct);
                    if (result.Success) Interlocked.Increment(ref imported);
                    else
                    {
                        Interlocked.Increment(ref failed);
                        errors.Enqueue(new RosterPullFailure(roster.Cue, roster.SchoolYear, result.Error ?? "Unknown roster pull failure."));
                    }
                }

                var done = Interlocked.Increment(ref completed);
                if (done % 10 == 0 || done == total)
                {
                    await progressLock.WaitAsync(ct);
                    try
                    {
                        await WriteProgressAsync("rosters", Volatile.Read(ref completed), total,
                            skipped + Volatile.Read(ref skippedRosters), ct);
                    }
                    finally { progressLock.Release(); }
                }
            });

            skipped += skippedRosters;
            if (failed > 0)
            {
                await WriteProgressAsync("rosters", completed, total, skipped, cancellationToken);
                var firstErrors = errors.Take(5).ToArray();
                foreach (var failure in firstErrors)
                    logger.LogError("Roster pull failed for CUE {Cue}, school year {SchoolYear}: {Error}",
                        failure.Cue, failure.SchoolYear, failure.Error);
                var reason = firstErrors.Length == 0 ? "Hubo un problema de conexión o almacenamiento local." : SummarizeFailure(firstErrors[0].Error);
                return new(false, imported, total, skipped,
                    $"{failed} listas no se pudieron descargar. {reason} Reintentá la descarga.");
            }

            await syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"),
                "last_full_roster_pull_at", JsonSerializer.Serialize(DateTimeOffset.UtcNow, JsonOptions),
                DateTimeOffset.UtcNow.ToString("O")), cancellationToken);
            var previousServerTime = await ReadStateStringAsync("last_server_time", cancellationToken);
            if (index.ServerTime != default &&
                (!DateTimeOffset.TryParse(previousServerTime, out var previous) || index.ServerTime > previous))
            {
                await syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"),
                    "last_server_time", JsonSerializer.Serialize(index.ServerTime, JsonOptions),
                    DateTimeOffset.UtcNow.ToString("O")), cancellationToken);
            }
            await WriteProgressAsync("complete", completed, total, skipped, cancellationToken);
            return new(true, imported, total, skipped, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Full roster import failed before all CUEs completed.");
            return new(false, 0, 0, 0,
                $"No se pudo completar la descarga. {SummarizeFailure(exception.Message)} Reintentá la descarga.");
        }
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

    private Task WriteProgressAsync(string phase, int completed, int total, int skipped, CancellationToken cancellationToken) =>
        syncStateRepository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "activation_download_progress",
            JsonSerializer.Serialize(new ActivationDownloadProgress(phase, completed, total, skipped), JsonOptions),
            DateTimeOffset.UtcNow.ToString("O")), cancellationToken);

    private static LocalRosterPullResult Failure(string error) =>
        new(false, false, null, null, 0, 0, error);

    private static string SummarizeFailure(string error)
    {
        if (error.Contains("DocumentHmacKey", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("document key", StringComparison.OrdinalIgnoreCase))
            return "No se pudo preparar la clave local de privacidad.";
        if (error.Contains("checksum", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("different CUE", StringComparison.OrdinalIgnoreCase))
            return "La lista recibida no superó la validación.";
        if (error.Contains("SQLite", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("storage", StringComparison.OrdinalIgnoreCase))
            return "No se pudo guardar una lista en este equipo.";
        if (error.Contains("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            var marker = error.IndexOf("HTTP", StringComparison.OrdinalIgnoreCase);
            var status = new string(error[(marker + 4)..].TrimStart().TakeWhile(char.IsDigit).Take(3).ToArray());
            return status.Length > 0
                ? $"Central no pudo entregar una lista (HTTP {status})."
                : "Central no pudo entregar una lista.";
        }
        return "Hubo un problema de conexión o almacenamiento local.";
    }

    private sealed record RosterIndex(DateTimeOffset ServerTime, List<SchoolEntry> Schools, List<RosterEntry> Rosters);
    private sealed record SchoolEntry(string Cue, string? Name);
    private sealed record RosterEntry(string Cue, string SchoolYear);
    private sealed record RosterPullFailure(string Cue, string SchoolYear, string Error);
}

public sealed record LocalRosterPullResult(
    bool Success,
    bool Imported,
    string? SnapshotId,
    string? Checksum,
    int SectionCount,
    int StudentCount,
    string? Error);

public sealed record LocalRosterBulkPullResult(bool Success, int Downloaded, int Total, int Skipped, string? Error);

public sealed record ActivationDownloadProgress(string Phase, int Completed, int Total, int Skipped);

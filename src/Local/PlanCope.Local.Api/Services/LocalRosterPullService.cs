using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class LocalRosterPullService(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    ILocalRosterRepository rosterRepository,
    IDocumentHmacService documentHmacService)
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
                return Failure($"Central roster pull failed: {(int)response.StatusCode} {error}");
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
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Failure(exception.Message);
        }
    }

    public async Task<LocalRosterBulkPullResult> PullAllAsync(CancellationToken cancellationToken = default)
    {
        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var nodeId = await ReadStateStringAsync("node_id", cancellationToken);
        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
            return new(false, 0, 0, "Central credentials are not configured.");
        if (!Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress))
            return new(false, 0, 0, "Central URL is invalid.");

        try
        {
            var client = httpClientFactory.CreateClient(nameof(LocalRosterPullService));
            client.BaseAddress = baseAddress;
            using var response = await client.GetAsync("api/sync/rosters/index", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, 0, 0, $"Central roster index failed: {(int)response.StatusCode}.");
            var index = await response.Content.ReadFromJsonAsync<RosterIndex>(JsonOptions, cancellationToken);
            if (index is null)
                return new(false, 0, 0, "Central returned an empty roster index.");

            await rosterRepository.UpsertSchoolsAsync(index.Schools
                .Select(static school => new LocalSchoolSummary(school.Cue, school.Name)).ToArray(), cancellationToken);
            var imported = 0;
            foreach (var roster in index.Rosters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await PullAsync(roster.Cue, roster.SchoolYear, cancellationToken);
                if (!result.Success)
                    return new(false, imported, index.Rosters.Count, result.Error);
                imported++;
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
            return new(true, imported, index.Rosters.Count, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(false, 0, 0, exception.Message);
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

    private static LocalRosterPullResult Failure(string error) =>
        new(false, false, null, null, 0, 0, error);

    private sealed record RosterIndex(DateTimeOffset ServerTime, List<SchoolEntry> Schools, List<RosterEntry> Rosters);
    private sealed record SchoolEntry(string Cue, string? Name);
    private sealed record RosterEntry(string Cue, string SchoolYear);
}

public sealed record LocalRosterPullResult(
    bool Success,
    bool Imported,
    string? SnapshotId,
    string? Checksum,
    int SectionCount,
    int StudentCount,
    string? Error);

public sealed record LocalRosterBulkPullResult(bool Success, int Downloaded, int Total, string? Error);

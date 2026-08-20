using System.Net.Http.Headers;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

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
        cue = cue?.Trim().ToUpperInvariant() ?? string.Empty;
        schoolYear = schoolYear?.Trim() ?? string.Empty;
        if (cue.Length == 0 || cue.Length > GeRosterTransportLimits.MaxCueLength ||
            schoolYear.Length == 0 || schoolYear.Length > GeRosterTransportLimits.MaxSchoolYearLength)
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
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

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
}

public sealed record LocalRosterPullResult(
    bool Success,
    bool Imported,
    string? SnapshotId,
    string? Checksum,
    int SectionCount,
    int StudentCount,
    string? Error);

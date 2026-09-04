using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

/// <summary>
/// Sends the local outbox only when explicitly invoked by an operator or release.
/// There is intentionally no hosted service, timer, polling loop, or startup push.
/// </summary>
public sealed class LocalOutboxPushService(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    IOutboxRepository outboxRepository)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LocalOutboxPushResult> PushAsync(int requestedLimit, CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(requestedLimit, 1, 200);
        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var nodeId = await ReadStateStringAsync("node_id", cancellationToken);
        var token = await ReadStateStringAsync("central_access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(nodeId))
        {
            return new(false, 0, 0, 0, "central_url and node_id must be configured in sync_state.");
        }

        if (!Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("http" or "https"))
        {
            return new(false, 0, 0, 0, "central_url is invalid.");
        }

        var now = DateTimeOffset.UtcNow;
        var pending = await outboxRepository.GetPendingBatchAsync(limit, now.ToString("O"), cancellationToken);
        if (pending.Count == 0)
        {
            return new(true, 0, 0, 0, null);
        }

        var pushItems = new List<PushItem>(pending.Count);
        var validatedPending = new List<SyncOutbox>(pending.Count);
        foreach (var item in pending)
        {
            try
            {
                using var document = JsonDocument.Parse(item.PayloadJson);
                if (item.EventType is SyncEventTypes.AttemptSubmitted && ContainsForbiddenProperty(document.RootElement))
                {
                    throw new InvalidOperationException("An attempt payload contains a prohibited document or token field.");
                }

                var payload = document.RootElement.Clone();
                pushItems.Add(new PushItem(
                    item.IdempotencyKey,
                    item.EventType,
                    item.AggregateType,
                    item.AggregateId,
                    payload,
                    SyncPayloadChecksum.Calculate(payload),
                    item.CreatedAt));
                validatedPending.Add(item);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                await RequeueAsync(item, exception.Message, cancellationToken);
            }
        }

        if (pushItems.Count == 0)
        {
            return new(false, pending.Count, 0, pending.Count, "No pending payload could be validated.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(LocalOutboxPushService));
            client.BaseAddress = baseAddress;
            client.DefaultRequestHeaders.Remove("X-Node-Id");
            client.DefaultRequestHeaders.Add("X-Node-Id", nodeId.Trim());
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await client.PostAsJsonAsync("api/sync/push", new PushRequest(nodeId.Trim(), pushItems), JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                foreach (var item in validatedPending)
                {
                    await RequeueAsync(item, $"Central push failed: {(int)response.StatusCode} {error}", cancellationToken);
                }

                return new(false, pending.Count, 0, pending.Count, $"Central push failed: {(int)response.StatusCode}.");
            }

            var result = await response.Content.ReadFromJsonAsync<PushResponse>(JsonOptions, cancellationToken);
            if (result is null)
            {
                throw new InvalidOperationException("Central push returned an empty response.");
            }

            var accepted = 0;
            var failed = pending.Count - validatedPending.Count;
            foreach (var item in validatedPending)
            {
                var itemResult = result.Results.FirstOrDefault(candidate => candidate.IdempotencyKey == item.IdempotencyKey);
                if (itemResult?.Status is "accepted" or "duplicate")
                {
                    await outboxRepository.MarkSentAsync(item.Id, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
                    accepted++;
                }
                else
                {
                    await RequeueAsync(item, itemResult?.Reason ?? "Central did not accept the item.", cancellationToken);
                    failed++;
                }
            }

            if (accepted > 0)
            {
                await syncStateRepository.UpsertAsync(new SyncState(
                    Guid.NewGuid().ToString("N"),
                    "last_push_at",
                    JsonSerializer.Serialize(DateTimeOffset.UtcNow, JsonOptions),
                    DateTimeOffset.UtcNow.ToString("O")), cancellationToken);
            }

            return new(failed == 0, pending.Count, accepted, failed, failed == 0 ? null : "Some items remain pending for a later manual retry.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            foreach (var item in validatedPending)
            {
                await RequeueAsync(item, exception.Message, cancellationToken);
            }

            return new(false, pending.Count, 0, pending.Count, exception.Message);
        }
    }

    private async Task RequeueAsync(SyncOutbox item, string error, CancellationToken cancellationToken)
    {
        var retryCount = item.RetryCount + 1;
        var delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Math.Min(retryCount, 10))));
        await outboxRepository.RequeueAsync(
            item.Id,
            retryCount,
            DateTimeOffset.UtcNow.Add(delay).ToString("O"),
            error.Length > 1000 ? error[..1000] : error,
            cancellationToken);
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

    private static bool ContainsForbiddenProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsForbiddenPropertyName(property.Name))
                {
                    return true;
                }

                if (ContainsForbiddenProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsForbiddenProperty);
        }

        return false;
    }

    private static bool IsForbiddenPropertyName(string name) =>
        name.Equals("document", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("documentHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("tokenHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("resolutionToken", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dni", StringComparison.OrdinalIgnoreCase);
}

public sealed record LocalOutboxPushResult(bool Success, int Considered, int Accepted, int Pending, string? Error);

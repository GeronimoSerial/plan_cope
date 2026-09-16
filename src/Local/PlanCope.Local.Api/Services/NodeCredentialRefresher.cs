using System.Net.Http.Json;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

public sealed class NodeCredentialRefresher(
    IHttpClientFactory httpClientFactory,
    ISyncStateRepository syncStateRepository,
    INodeIdentityRepository nodeIdentityRepository)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var centralUrl = await ReadStateStringAsync("central_url", cancellationToken);
        var refreshToken = await ReadStateStringAsync("central_refresh_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(centralUrl) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var client = httpClientFactory.CreateClient(nameof(NodeCredentialRefresher));
        client.BaseAddress = new Uri(centralUrl.Trim().TrimEnd('/') + "/");
        HttpResponseMessage response;
        ActivationRefreshResponse? refreshed;
        try
        {
            response = await client.PostAsJsonAsync("api/activation/refresh", new ActivationRefreshRequest(refreshToken), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await MarkRevokedAsync(cancellationToken);
                return false;
            }
            refreshed = await response.Content.ReadFromJsonAsync<ActivationRefreshResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            await MarkRevokedAsync(cancellationToken);
            return false;
        }

        if (refreshed is null)
        {
            await MarkRevokedAsync(cancellationToken);
            return false;
        }

        await UpsertStateStringAsync("central_access_token", refreshed.AccessToken, cancellationToken);
        await UpsertStateStringAsync("central_refresh_token", refreshed.RefreshToken, cancellationToken);
        await UpsertStateStringAsync("central_access_token_expires_at", refreshed.AccessTokenExpiresAt.ToString("O"), cancellationToken);
        await UpsertStateStringAsync("central_refresh_token_expires_at", refreshed.RefreshTokenExpiresAt.ToString("O"), cancellationToken);

        if (refreshed.NodeRevoked)
        {
            await MarkRevokedAsync(cancellationToken);
            return false;
        }

        return true;
    }

    private async Task MarkRevokedAsync(CancellationToken cancellationToken)
    {
        var identity = await nodeIdentityRepository.GetAsync(cancellationToken);
        if (identity is null) return;
        await nodeIdentityRepository.UpsertAsync(identity with { CredentialState = "revoked" }, cancellationToken);
    }

    private async Task<string?> ReadStateStringAsync(string key, CancellationToken cancellationToken)
    {
        var state = await syncStateRepository.GetAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(state?.ValueJson)) return null;
        using var document = JsonDocument.Parse(state.ValueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }

    private Task UpsertStateStringAsync(string key, string value, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"), key, JsonSerializer.Serialize(value, JsonOptions), now), cancellationToken);
    }
}
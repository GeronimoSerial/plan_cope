using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Services;

public sealed class CentralCredentialHandler(ISyncStateRepository syncStateRepository, NodeCredentialRefresher refresher) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await AttachTokenAsync(request, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var refreshed = await refresher.TryRefreshAsync(cancellationToken);
        if (!refreshed)
        {
            return response;
        }

        var retryRequest = await CloneAsync(request);
        await AttachTokenAsync(retryRequest, cancellationToken);
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private async Task AttachTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = await syncStateRepository.GetAsync("central_access_token", cancellationToken);
        if (string.IsNullOrWhiteSpace(state?.ValueJson)) return;
        using var document = JsonDocument.Parse(state.ValueJson);
        var token = document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}
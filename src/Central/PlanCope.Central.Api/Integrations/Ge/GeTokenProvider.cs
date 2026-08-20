using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PlanCope.Central.Api.Integrations.Ge;

public sealed class GeTokenProvider(
    HttpClient httpClient,
    IOptions<GeApiOptions> options,
    GeTokenCache cache,
    TimeProvider? timeProvider = null) : IGeTokenProvider
{
    private readonly GeApiOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly GeTokenCache _cache = cache;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsCachedTokenValid())
        {
            return _cache.AccessToken!;
        }

        await _cache.RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCachedTokenValid())
            {
                return _cache.AccessToken!;
            }

            return await RefreshAsync(cancellationToken);
        }
        finally
        {
            _cache.RefreshLock.Release();
        }
    }

    public void Invalidate(string accessToken)
    {
        _cache.RefreshLock.Wait();
        try
        {
            // No borremos un token más nuevo que otro request ya pudo haber obtenido.
            if (string.Equals(_cache.AccessToken, accessToken, StringComparison.Ordinal))
            {
                _cache.AccessToken = null;
                _cache.ExpiresAt = default;
            }
        }
        finally
        {
            _cache.RefreshLock.Release();
        }
    }

    private async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("GeApi:Username and GeApi:Password must be configured before using GE.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _options.Username,
            ["password"] = _options.Password,
            ["grant_type"] = "password"
        });

        using var response = await httpClient.PostAsync("token", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GeApiException("GE token request failed.", response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var tokenResponse = await JsonSerializer.DeserializeAsync<GeTokenResponse>(responseStream, cancellationToken: cancellationToken);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new GeApiException("GE token response did not contain access_token.", response.StatusCode);
        }

        var safetyMargin = TimeSpan.FromSeconds(Math.Max(0, _options.TokenSafetyMarginSeconds));
        var lifetime = TimeSpan.FromSeconds(Math.Max(1, tokenResponse.ExpiresIn));
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime - safetyMargin);
        if (expiresAt <= _timeProvider.GetUtcNow())
        {
            expiresAt = _timeProvider.GetUtcNow().AddSeconds(1);
        }

        _cache.AccessToken = tokenResponse.AccessToken;
        _cache.ExpiresAt = expiresAt;
        return tokenResponse.AccessToken;
    }

    private bool IsCachedTokenValid()
    {
        return !string.IsNullOrWhiteSpace(_cache.AccessToken) &&
               _cache.ExpiresAt > _timeProvider.GetUtcNow();
    }
}

public sealed class GeApiException(string message, System.Net.HttpStatusCode? statusCode = null)
    : HttpRequestException(message, inner: null, statusCode);

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Middleware;

/// <summary>
/// Rate-limits POST /api/activation/redeem per client IP and per key prefix. This is a lockout,
/// not a smooth refill: once a window's limit is hit, attempts are refused for the remainder of
/// the window. Standalone pipeline middleware so it stays fully decoupled from the activation
/// controller it protects.
/// </summary>
public sealed class ActivationRateLimitMiddleware(RequestDelegate next, IMemoryCache cache)
{
    // Window for both counters: 5 minutes.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    // 10 attempts per window per client IP: catches one IP hammering many different keys.
    private const int MaxAttemptsPerIp = 10;

    // 5 attempts per window per key prefix: catches one leaked key hammered from many IPs.
    private const int MaxAttemptsPerKeyPrefix = 5;

    // Matches ActivationKeyService.PrefixLength: the stored KeyPrefix is the first 8 characters.
    private const int KeyPrefixLength = 8;

    private const string RedeemPath = "/api/activation/redeem";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsRedeemRequest(context))
        {
            await next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip))
        {
            // No usable client IP means no rate limiting is possible; refuse instead of bypassing.
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        var keyPrefix = await ReadKeyPrefixAsync(context.Request);
        if (IsLockedOut($"activation-redeem:ip:{ip}", MaxAttemptsPerIp) ||
            (keyPrefix is not null && IsLockedOut($"activation-redeem:key:{keyPrefix}", MaxAttemptsPerKeyPrefix)))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        await next(context);
    }

    private static bool IsRedeemRequest(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method) &&
               string.Equals(context.Request.Path.Value, RedeemPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Peeks the plaintext activation key from the request body without consuming it: the body is
    /// buffered and rewound so the downstream controller can still read it normally.
    /// </summary>
    private static async Task<string?> ReadKeyPrefixAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("activationKey", out var keyElement))
            {
                return null;
            }

            var rawKey = keyElement.GetString();
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                // Missing/unparseable key: let it through to the controller, which rejects the
                // malformed key with its own validation.
                return null;
            }

            return ActivationKeyService.TryNormalize(rawKey, out var canonical)
                ? canonical[..KeyPrefixLength]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool IsLockedOut(string cacheKey, int limit)
    {
        var counter = cache.GetOrCreate(cacheKey, static entry =>
        {
            entry.SlidingExpiration = Window;
            return new LockoutCounter();
        });
        return (counter?.Increment() ?? 1) > limit;
    }

    private sealed class LockoutCounter
    {
        private int _value;

        public int Increment() => Interlocked.Increment(ref _value);
    }
}
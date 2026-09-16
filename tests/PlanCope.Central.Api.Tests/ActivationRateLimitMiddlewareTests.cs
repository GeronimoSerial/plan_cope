using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using PlanCope.Central.Api.Middleware;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// The middleware is a standalone pipeline component with no host dependency, so it is exercised
/// directly (per this project's direct-construction test convention) against a DefaultHttpContext.
/// </summary>
public sealed class ActivationRateLimitMiddlewareTests
{
    // A real generated key so the prefix extraction exercises ActivationKeyService.TryNormalize
    // end to end. A fresh Argon2id run per test class instance is acceptable.
    private static readonly string ValidKey = new ActivationKeyService().Generate().PlaintextKey;

    // Mirrors ActivationRateLimitMiddleware.MaxAttemptsPerKeyPrefix: the request that pushes a
    // window over the limit is the one refused.
    private const int PrefixWindowLimit = 5;

    [Fact]
    public async Task RedeemRequests_UpToWindowLimitPassThrough_AndExceedingRequestReturns429()
    {
        var passedThrough = 0;
        var middleware = new ActivationRateLimitMiddleware(
            _ => { passedThrough++; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()));

        for (var attempt = 1; attempt <= PrefixWindowLimit; attempt++)
        {
            var context = await InvokeRedeemAsync(middleware, ValidKey);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        Assert.Equal(PrefixWindowLimit, passedThrough);

        var blocked = await InvokeRedeemAsync(middleware, ValidKey);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.Equal(PrefixWindowLimit, passedThrough);
    }

    [Fact]
    public async Task RedeemRequests_ForDifferentKeyPrefixes_HaveIndependentWindows()
    {
        var otherKey = new ActivationKeyService().Generate().PlaintextKey;
        Assert.NotEqual(ValidKey[..8], otherKey[..8]);

        var passedThrough = 0;
        var middleware = new ActivationRateLimitMiddleware(
            _ => { passedThrough++; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()));

        // Each prefix is exercised from its own IP so the per-IP counter never trips the test.
        for (var attempt = 1; attempt <= PrefixWindowLimit; attempt++)
        {
            await InvokeRedeemAsync(middleware, ValidKey, IPAddress.Parse("127.0.0.1"));
            await InvokeRedeemAsync(middleware, otherKey, IPAddress.Parse("127.0.0.2"));
        }

        Assert.Equal(PrefixWindowLimit * 2, passedThrough);

        var blocked = await InvokeRedeemAsync(middleware, ValidKey, IPAddress.Parse("127.0.0.1"));
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task RedeemRequest_WithMalformedKey_PassesThroughToController()
    {
        var passedThrough = 0;
        var middleware = new ActivationRateLimitMiddleware(
            _ => { passedThrough++; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()));

        for (var attempt = 1; attempt <= PrefixWindowLimit + 1; attempt++)
        {
            var context = await InvokeRedeemAsync(middleware, "not-a-key");
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        Assert.Equal(PrefixWindowLimit + 1, passedThrough);
    }

    [Fact]
    public async Task NonRedeemRequest_IsNeverRateLimited()
    {
        var passedThrough = 0;
        var middleware = new ActivationRateLimitMiddleware(
            _ => { passedThrough++; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()));

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/health/live";
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        Assert.Equal(12, passedThrough);
    }

    private static async Task<HttpContext> InvokeRedeemAsync(ActivationRateLimitMiddleware middleware, string? key, IPAddress? ip = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ip ?? IPAddress.Loopback;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/activation/redeem";
        context.Request.ContentType = "application/json";
        var body = key is null ? "{}" : $"{{\"activationKey\":\"{key}\"}}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await middleware.InvokeAsync(context);

        return context;
    }
}
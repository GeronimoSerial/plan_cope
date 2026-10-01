using System.Net;
using Xunit;
using PlanCope.Local.Host.Services;

namespace PlanCope.Local.Host.Tests;

public sealed class UpdateRequestAuthTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ShouldRefresh_WhenExpiryIsWithinTwoMinutes(int minutes)
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(UpdateRequestAuth.ShouldRefresh(now.AddMinutes(minutes), now));
    }

    [Fact]
    public void ShouldNotRefresh_WhenTokenHasMoreThanTwoMinutesLeft()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(UpdateRequestAuth.ShouldRefresh(now.AddMinutes(3), now));
    }

    [Fact]
    public async Task RunAsync_RefreshesThenRetriesOnceAfterUnauthorized()
    {
        var refreshRequests = new List<bool>();
        var requestCount = 0;

        var result = await UpdateRequestAuth.RunAsync(
            () =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    throw new HttpRequestException("expired", null, HttpStatusCode.Unauthorized);
                }

                return Task.FromResult("updated");
            },
            forceRefresh =>
            {
                refreshRequests.Add(forceRefresh);
                return Task.CompletedTask;
            });

        Assert.Equal("updated", result);
        Assert.Equal(2, requestCount);
        Assert.Equal(new[] { false, true }, refreshRequests);
    }

    [Fact]
    public async Task RunAsync_DoesNotRefreshOrRetryAfterForbidden()
    {
        var refreshRequests = new List<bool>();
        var requestCount = 0;

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => UpdateRequestAuth.RunAsync(
            () =>
            {
                requestCount++;
                throw new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
            },
            forceRefresh =>
            {
                refreshRequests.Add(forceRefresh);
                return Task.CompletedTask;
            }));

        Assert.Equal("forbidden", exception.Message);
        Assert.Equal(1, requestCount);
        Assert.Equal(new[] { false }, refreshRequests);
    }
}

using System.Net;

namespace PlanCope.Local.Host.Services;

/// <summary>
/// Shares the update request authentication policy with unit tests: refresh expiring
/// credentials before use and retry one request once after an authorization failure.
/// </summary>
public static class UpdateRequestAuth
{
    public static bool ShouldRefresh(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is null || expiresAt <= now.AddMinutes(2);

    public static async Task<T> RunAsync<T>(
        Func<Task<T>> request,
        Func<bool, Task> refreshToken)
    {
        await refreshToken(false).ConfigureAwait(false);
        try
        {
            return await request().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnauthorized(exception))
        {
            await refreshToken(true).ConfigureAwait(false);
            return await request().ConfigureAwait(false);
        }
    }

    public static async Task RunAsync(Func<Task> request, Func<bool, Task> refreshToken)
    {
        await RunAsync(async () =>
        {
            await request().ConfigureAwait(false);
            return true;
        }, refreshToken).ConfigureAwait(false);
    }

    private static bool IsUnauthorized(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            {
                return true;
            }
        }

        return false;
    }
}

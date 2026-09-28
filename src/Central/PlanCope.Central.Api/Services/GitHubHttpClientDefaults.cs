using System.Net.Http.Headers;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Headers GitHub's REST API requires on every request to the private installer repo, extracted
/// into one place so Program.cs's real DI registration and this project's tests build the HttpClient
/// identically — a test that duplicated the header values separately could pass while the real
/// registration silently drifted out of sync.
/// </summary>
public static class GitHubHttpClientDefaults
{
    // GitHub's REST API returns 403 Forbidden for any request with no User-Agent at all — this is
    // not optional, and it is not the same as an auth failure (the token can be perfectly valid).
    public const string UserAgent = "PlanCope-Central";

    public const string ApiVersion = "2022-11-28";

    public static void Apply(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, null));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }
}

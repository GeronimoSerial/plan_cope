using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Real installer backend backed by the Releases API of the PRIVATE GitHub repository that
/// scripts/publish-private-installer.ps1 (Unit 4) uploads to, using that script's conventions:
/// release tag = version (e.g. "1.2.3"); channel "beta" releases are GitHub prereleases while
/// channel "stable" releases are not.
///
/// The returned <see cref="InstallerReference.DownloadUrl"/> is the GitHub API asset download
/// URL, which is private and requires the configured token to actually fetch. A browser
/// authenticated to Central (but not to GitHub) cannot follow it directly; a truly public
/// download experience would require this endpoint to proxy the asset bytes with the
/// server-side token instead of handing the raw private GitHub URL to the client. Implementing
/// that proxy is out of scope for B7 unit 8 (documented as a known limitation).
/// </summary>
public sealed class GitHubReleaseInstallerStorage(
    HttpClient httpClient,
    IOptions<InstallerStorageOptions> options,
    ILogger<GitHubReleaseInstallerStorage> logger) : IInstallerStorage
{
    public async Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken)
    {
        var installerOptions = options.Value;
        if (string.IsNullOrWhiteSpace(installerOptions.Repo) || string.IsNullOrWhiteSpace(installerOptions.Token))
        {
            logger.LogWarning("Installer repo/token is not configured — set PLANCOPE_PRIVATE_INSTALLER_REPO / INSTALLER_REPO_TOKEN.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{installerOptions.Repo}/releases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installerOptions.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GitHub Releases API for {Repo} responded {StatusCode} {ReasonPhrase}; no installer available.",
                    installerOptions.Repo,
                    (int)response.StatusCode,
                    response.ReasonPhrase);
                return null;
            }

            // The Releases API does not expose a SHA-256 digest of assets; the field is therefore
            // populated with an empty string unless a checksum companion is ever added to releases.
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(payload);

            InstallerReference? latest = null;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryReadRelease(element, channel, out var candidate))
                {
                    continue;
                }

                if (latest is null || candidate.PublishedAt > latest.PublishedAt)
                {
                    latest = candidate;
                }
            }

            return latest;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "GitHub Releases API call for {Repo} failed; no installer available.", installerOptions.Repo);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "GitHub Releases API for {Repo} returned unparseable JSON; no installer available.", installerOptions.Repo);
            return null;
        }
    }

    private static bool TryReadRelease(JsonElement release, string channel, out InstallerReference reference)
    {
        reference = null!;

        if (!release.TryGetProperty("tag_name", out var tagElement) ||
            string.IsNullOrWhiteSpace(tagElement.GetString()))
        {
            return false;
        }

        var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseElement) &&
                           prereleaseElement.ValueKind == JsonValueKind.True;

        var matchesChannel = channel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? isPrerelease
            : !isPrerelease;
        if (!matchesChannel)
        {
            return false;
        }

        if (!release.TryGetProperty("published_at", out var publishedElement) ||
            !DateTimeOffset.TryParse(
                publishedElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var publishedAt))
        {
            return false;
        }

        if (!TryGetInstallerAssetUrl(release, out var assetUrl))
        {
            return false;
        }

        reference = new InstallerReference(
            tagElement.GetString()!,
            channel.ToLowerInvariant(),
            assetUrl,
            Sha256: string.Empty,
            publishedAt);
        return true;
    }

    private static bool TryGetInstallerAssetUrl(JsonElement release, out Uri assetUrl)
    {
        assetUrl = null!;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            // GitHub auto-generates source archives that are unrelated to the installer.
            if (name is "Source code (zip)" or "Source code (tar.gz)")
            {
                continue;
            }

            if (asset.TryGetProperty("url", out var urlElement) &&
                Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var parsed))
            {
                assetUrl = parsed;
                return true;
            }
        }

        return false;
    }
}
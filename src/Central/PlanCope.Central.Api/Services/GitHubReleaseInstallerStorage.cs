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
/// <see cref="InstallerReference.DownloadUrl"/> is a path on THIS API (api/downloads/installer/file),
/// never the raw GitHub asset URL — a browser cannot authenticate against GitHub directly, so
/// <see cref="GetLatestDownloadAsync"/> streams the asset bytes through this server using the
/// configured token instead of handing the client a private URL to follow itself.
/// </summary>
public sealed class GitHubReleaseInstallerStorage(
    HttpClient httpClient,
    IOptions<InstallerStorageOptions> options,
    ILogger<GitHubReleaseInstallerStorage> logger) : IInstallerStorage
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.Repo) && !string.IsNullOrWhiteSpace(options.Value.Token);

    public async Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken)
    {
        var found = await FindLatestAssetAsync(channel, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        return new InstallerReference(
            found.Value.Tag,
            channel.ToLowerInvariant(),
            BuildDownloadPath(channel),
            Sha256: string.Empty,
            found.Value.PublishedAt);
    }

    public async Task<InstallerDownload?> GetLatestDownloadAsync(string channel, CancellationToken cancellationToken)
    {
        var found = await FindLatestAssetAsync(channel, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, found.Value.AssetUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        // Accept: application/octet-stream on the asset API URL is what makes GitHub redirect to
        // the actual blob storage location instead of returning the asset's JSON metadata. That
        // redirect goes to a different host, and the default HttpClient handler does not forward
        // the Authorization header across a cross-host redirect — do not set AllowAutoRedirect to
        // false and do not manually re-attach the token after a redirect; either would break this
        // or leak the token to a third-party host.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "GitHub asset download for {AssetUrl} responded {StatusCode} {ReasonPhrase}; no installer available.",
                found.Value.AssetUrl,
                (int)response.StatusCode,
                response.ReasonPhrase);
            response.Dispose();
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var contentLength = response.Content.Headers.ContentLength;
        return new InstallerDownload(response, stream, contentType, found.Value.AssetName, contentLength);
    }

    public async Task<InstallerDownload?> GetAssetDownloadAsync(string assetName, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{options.Value.Repo}/releases?per_page=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var releasesResponse = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!releasesResponse.IsSuccessStatusCode) return null;

        using var document = JsonDocument.Parse(await releasesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Uri? assetUrl = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), assetName, StringComparison.Ordinal) &&
                    asset.TryGetProperty("url", out var url) &&
                    Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsedUrl))
                {
                    assetUrl = parsedUrl;
                    break;
                }
            }
            if (assetUrl is not null) break;
        }

        if (assetUrl is null) return null;
        using var assetRequest = new HttpRequestMessage(HttpMethod.Get, assetUrl);
        assetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        assetRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        var response = await httpClient.SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new InstallerDownload(
            response,
            stream,
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            assetName,
            response.Content.Headers.ContentLength);
    }

    private async Task<FoundAsset?> FindLatestAssetAsync(string channel, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Installer repo/token is not configured — set PLANCOPE_PRIVATE_INSTALLER_REPO / INSTALLER_REPO_TOKEN.");
            return null;
        }

        var installerOptions = options.Value;
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

            FoundAsset? latest = null;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryReadRelease(element, channel, out var candidate))
                {
                    continue;
                }

                if (latest is null || candidate.PublishedAt > latest.Value.PublishedAt)
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

    private static bool TryReadRelease(JsonElement release, string channel, out FoundAsset asset)
    {
        asset = default;

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

        if (!TryGetInstallerAsset(release, out var assetName, out var assetUrl))
        {
            return false;
        }

        asset = new FoundAsset(tagElement.GetString()!, publishedAt, assetName, assetUrl);
        return true;
    }

    private static bool TryGetInstallerAsset(JsonElement release, out string assetName, out Uri assetUrl)
    {
        assetName = string.Empty;
        assetUrl = null!;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

            // Match the installer by extension instead of taking the first asset that is not a
            // source archive. The private release repo is not guaranteed to hold only installers:
            // it has also carried the encrypted roster bundle (`*.enc`), and "first asset that
            // isn't source code" would have handed that file to every authenticated operator from
            // the Descargas page, labelled as the desktop installer. An allow-list fails closed —
            // an unrecognised asset yields no installer rather than the wrong one.
            if (!IsInstallerAsset(name))
            {
                continue;
            }

            if (asset.TryGetProperty("url", out var urlElement) &&
                Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var parsed))
            {
                assetName = name!;
                assetUrl = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool IsInstallerAsset(string? name)
    {
        // scripts/publish-private-installer.ps1 uploads the Velopack output, whose file name ends
        // in .exe (setup) or .msi. Nothing else in a release is an installer.
        return !string.IsNullOrWhiteSpace(name) &&
               (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
    }

    private static Uri BuildDownloadPath(string channel) =>
        new($"/api/downloads/installer/file?channel={Uri.EscapeDataString(channel.ToLowerInvariant())}", UriKind.Relative);

    private readonly record struct FoundAsset(string Tag, DateTimeOffset PublishedAt, string AssetName, Uri AssetUrl);
}

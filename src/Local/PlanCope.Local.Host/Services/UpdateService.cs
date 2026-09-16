using System.Net.Http.Headers;
using System.Security.Cryptography;
using Velopack.Locators;

namespace PlanCope.Local.Host.Services;

public enum UpdateChannel
{
    Stable,
    Beta,
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string? TargetVersion);

/// <summary>
/// Thin seam over Velopack's UpdateManager so UpdateService is testable without a
/// real update feed. The production implementation wraps Velopack.UpdateManager.
/// </summary>
public interface IUpdateBackend
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the pending update and verifies the on-disk payload's SHA-256 against
    /// <paramref name="expectedSha256"/> (case-insensitive hex compare). Returns true only
    /// when the download completed and the checksum matches. Returns false on a
    /// checksum mismatch or when the downloaded file cannot be verified — it never throws
    /// for an integrity failure, so the caller can report the result cleanly.
    /// </summary>
    Task<bool> DownloadUpdatesAsync(string expectedSha256, CancellationToken cancellationToken);

    void ApplyUpdatesAndRestart();
}

/// <summary>
/// Velopack IFileDownloader that attaches an <c>Authorization: Bearer &lt;token&gt;</c> header
/// to every request. Velopack's built-in SimpleWebSource has no bearer-token hook (the
/// <c>authorization</c> parameter it passes to the downloader is always null), so node-access
/// auth has to come from the downloader. The token is read fresh from
/// <paramref name="accessTokenProvider"/> on every request rather than cached at construction
/// time, because the token can rotate while the backend is long-lived.
/// </summary>
internal sealed class BearerAuthFileDownloader : Velopack.Sources.HttpClientFileDownloader
{
    private readonly Func<string?> _accessTokenProvider;

    public BearerAuthFileDownloader(Func<string?> accessTokenProvider)
    {
        _accessTokenProvider = accessTokenProvider;
    }

    /// <inheritdoc cref="Velopack.Sources.HttpClientFileDownloader.CreateHttpClient(string?, string?, double)" />
    protected override HttpClient CreateHttpClient(string? authorization, string? accept, double timeout)
    {
        // SimpleWebSource passes null here; ignore whatever it gives us and attach the
        // node access token ourselves, when the provider has one to give.
        var client = base.CreateHttpClient(authorization: null, accept, timeout);
        var token = _accessTokenProvider();
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}

/// <summary>
/// Production IUpdateBackend backed by Velopack.UpdateManager. Construct with the Central
/// update feed's base URL, the feed channel ("stable" or "beta") and a provider for the
/// node-access bearer token. The channel passed to <see cref="CheckForUpdatesAsync"/> is set
/// per-call via <c>UpdateOptions.ExplicitChannel</c>; the constructor's channel is used for
/// the download/apply managers, which have no per-call channel of their own.
/// </summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly string _feedBaseUrl;
    private readonly string _channel;
    private readonly Func<string?> _accessTokenProvider;
    private Velopack.UpdateInfo? _pendingUpdate;

    public VelopackUpdateBackend(string feedBaseUrl, string channel, Func<string?> accessTokenProvider)
    {
        _feedBaseUrl = feedBaseUrl;
        _channel = channel;
        _accessTokenProvider = accessTokenProvider;
    }

    private Velopack.UpdateManager CreateManager(string explicitChannel)
    {
        var downloader = new BearerAuthFileDownloader(_accessTokenProvider);
        var source = new Velopack.Sources.SimpleWebSource(_feedBaseUrl, downloader, timeout: 1.0);
        return new Velopack.UpdateManager(source, new Velopack.UpdateOptions { ExplicitChannel = explicitChannel }, locator: null);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var manager = CreateManager(channel == UpdateChannel.Beta ? "beta" : "stable");
        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _pendingUpdate = info;
        return new UpdateCheckResult(info is not null, info?.TargetFullRelease.Version.ToString());
    }

    public async Task<bool> DownloadUpdatesAsync(string expectedSha256, CancellationToken cancellationToken)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("Call CheckForUpdatesAsync first and confirm an update is available.");
        }

        var manager = CreateManager(_channel);
        await manager.DownloadUpdatesAsync(_pendingUpdate).ConfigureAwait(false);

        if (!VelopackLocator.IsCurrentSet)
        {
            return false;
        }

        var packagePath = Path.Combine(VelopackLocator.Current.PackagesDir ?? string.Empty, _pendingUpdate.TargetFullRelease.FileName);
        return Sha256Matches(packagePath, expectedSha256);
    }

    /// <summary>
    /// Computes the SHA-256 of <paramref name="filePath"/> (uppercase hex) and compares it
    /// to <paramref name="expectedSha256"/> case-insensitively. Fails closed: returns false
    /// when the file is missing or no expected hash was supplied.
    /// </summary>
    internal static bool Sha256Matches(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(filePath))
        {
            return false;
        }

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    public void ApplyUpdatesAndRestart()
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("No downloaded update to apply.");
        }

        var manager = CreateManager(_channel);
        manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}

/// <summary>
/// Drives the update lifecycle: a non-blocking check (call after the main window is
/// operative, never during startup), a background download, and an explicit
/// user-confirmed restart. Never calls ApplyUpdatesAndRestart without confirmation.
/// </summary>
public sealed class UpdateService
{
    private readonly IUpdateBackend _backend;
    private bool _downloadReady;

    public UpdateService(IUpdateBackend backend, UpdateChannel channel)
    {
        _backend = backend;
        Channel = channel;
    }

    public UpdateChannel Channel { get; }

    /// <summary>
    /// True when the most recent download attempt completed but failed SHA-256 integrity
    /// verification. Lets a caller distinguish "no update downloaded yet" (also
    /// <see cref="TryApplyAndRestart"/> returning false) from "download failed integrity
    /// check" for reporting purposes. Reset to false at the start of each download attempt.
    /// </summary>
    public bool LastDownloadIntegrityFailed { get; private set; }

    /// <summary>
    /// Non-blocking: returns the in-flight Task immediately, does not block the
    /// calling (UI) thread while the check runs.
    /// </summary>
    public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        => _backend.CheckForUpdatesAsync(Channel, cancellationToken);

    /// <summary>
    /// Downloads the update in the background and verifies the downloaded payload's SHA-256
    /// against <paramref name="expectedSha256"/>. Does not restart anything. The update is
    /// only marked ready-to-apply when the backend confirms the checksum matched.
    /// </summary>
    public async Task DownloadUpdateAsync(string expectedSha256, CancellationToken cancellationToken = default)
    {
        LastDownloadIntegrityFailed = false;
        var verified = await _backend.DownloadUpdatesAsync(expectedSha256, cancellationToken).ConfigureAwait(false);
        _downloadReady = verified;
        LastDownloadIntegrityFailed = !verified;
    }

    /// <summary>
    /// Applies the downloaded update and restarts the app, but ONLY when
    /// <paramref name="userConfirmedRestart"/> is true. Returns false (no restart)
    /// if not confirmed, or if no update has been downloaded yet.
    /// </summary>
    public bool TryApplyAndRestart(bool userConfirmedRestart)
    {
        if (!userConfirmedRestart || !_downloadReady)
        {
            return false;
        }

        _backend.ApplyUpdatesAndRestart();
        return true;
    }
}
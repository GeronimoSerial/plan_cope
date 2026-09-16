using System.Net.Http.Headers;
using System.Security.Cryptography;
using Velopack.Locators;

namespace PlanCope.Local.Host.Services;

public enum UpdateChannel
{
    Stable,
    Beta,
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string? TargetVersion, string? Sha256, string? FileName);

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

    /// <summary>
    /// Re-applies a specific, previously-known-good package as a downgrade so a machine that
    /// failed to start after an update can revert automatically, then exits the process to
    /// restart into it. Returns false when a rollback is not possible (not running as an
    /// installed app, or the retained local package is missing or fails SHA-256) — the caller
    /// must treat a false return as "roll back later or not at all", never as a crash.
    /// </summary>
    bool TryRollBack(string version, string fileName, string sha256);
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
        return new UpdateCheckResult(info is not null, info?.TargetFullRelease.Version.ToString(), info?.TargetFullRelease.SHA256, info?.TargetFullRelease.FileName);
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

    /// <summary>
    /// Re-applies a specific, previously-known-good package as a downgrade. Mirrors
    /// <see cref="DownloadUpdatesAsync"/>'s local-package lookup: the package must exist in
    /// <c>VelopackLocator.Current.PackagesDir</c> and its SHA-256 must match
    /// <paramref name="sha256"/>. Re-downloading a missing or mismatched package is out of scope
    /// (an accepted limitation) — this method returns false and lets the caller proceed. On
    /// success it launches the Velopack updater and exits the process; nothing after it in the
    /// caller runs, matching <c>ApplyUpdatesAndRestart</c>'s documented behavior.
    /// </summary>
    public bool TryRollBack(string version, string fileName, string sha256)
    {
        if (!VelopackLocator.IsCurrentSet)
        {
            return false;
        }

        var packagePath = Path.Combine(VelopackLocator.Current.PackagesDir ?? string.Empty, fileName);
        if (!File.Exists(packagePath) || !Sha256Matches(packagePath, sha256))
        {
            return false;
        }

        var asset = new Velopack.VelopackAsset
        {
            PackageId = VelopackLocator.Current.AppId ?? string.Empty,
            Version = NuGet.Versioning.SemanticVersion.Parse(version),
            Type = Velopack.VelopackAssetType.Full,
            FileName = fileName,
            SHA256 = sha256,
            Size = new FileInfo(packagePath).Length,
        };

        CreateRollbackManager().ApplyUpdatesAndRestart(asset);
        return true;
    }

    /// <summary>
    /// Manager for the rollback path. <c>AllowVersionDowngrade</c> lets the feed treat an older
    /// release as installable; it is not consulted by <c>ApplyUpdatesAndRestart</c> itself (which
    /// applies the explicit asset) but keeps the rollback manager consistent with a downgrade.
    /// </summary>
    private Velopack.UpdateManager CreateRollbackManager()
    {
        var downloader = new BearerAuthFileDownloader(_accessTokenProvider);
        var source = new Velopack.Sources.SimpleWebSource(_feedBaseUrl, downloader, timeout: 1.0);
        return new Velopack.UpdateManager(source, new Velopack.UpdateOptions { ExplicitChannel = _channel, AllowVersionDowngrade = true }, locator: null);
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

    /// <summary>
    /// Re-applies a specific, previously-known-good package as a downgrade (the automatic-rollback
    /// path after a bad update). Wraps the backend exactly like the other three methods; the
    /// backend call exits the process on success.
    /// </summary>
    public bool TryRollBack(string version, string fileName, string sha256)
        => _backend.TryRollBack(version, fileName, sha256);
}
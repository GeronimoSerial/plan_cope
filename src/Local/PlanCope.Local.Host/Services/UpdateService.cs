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

    Task DownloadUpdatesAsync(CancellationToken cancellationToken);

    void ApplyUpdatesAndRestart();
}

/// <summary>
/// Production IUpdateBackend backed by Velopack.UpdateManager. Construct with the
/// configured update feed URL; ExplicitChannel is set per-call from the requested
/// UpdateChannel ("stable" or "beta").
/// </summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly string _updateUrl;
    private Velopack.UpdateInfo? _pendingUpdate;

    public VelopackUpdateBackend(string updateUrl)
    {
        _updateUrl = updateUrl;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var options = new Velopack.UpdateOptions
        {
            ExplicitChannel = channel == UpdateChannel.Beta ? "beta" : "stable",
        };
        var manager = new Velopack.UpdateManager(_updateUrl, options);
        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _pendingUpdate = info;
        return new UpdateCheckResult(info is not null, info?.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("Call CheckForUpdatesAsync first and confirm an update is available.");
        }

        var manager = new Velopack.UpdateManager(_updateUrl);
        await manager.DownloadUpdatesAsync(_pendingUpdate).ConfigureAwait(false);
    }

    public void ApplyUpdatesAndRestart()
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("No downloaded update to apply.");
        }

        var manager = new Velopack.UpdateManager(_updateUrl);
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
    /// Non-blocking: returns the in-flight Task immediately, does not block the
    /// calling (UI) thread while the check runs.
    /// </summary>
    public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        => _backend.CheckForUpdatesAsync(Channel, cancellationToken);

    /// <summary>Downloads the update in the background. Does not restart anything.</summary>
    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        await _backend.DownloadUpdatesAsync(cancellationToken).ConfigureAwait(false);
        _downloadReady = true;
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
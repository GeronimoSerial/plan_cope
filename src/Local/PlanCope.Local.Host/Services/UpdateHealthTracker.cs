using System.Text.Json;

namespace PlanCope.Local.Host.Services;

/// <summary>
/// Everything needed to reconstruct a Velopack asset for a previously-running, known-good
/// version so it can be re-applied as a downgrade after a bad update.
/// </summary>
public sealed record RollbackTarget(string Version, string FileName, string Sha256, string DownloadUrl);

/// <summary>
/// Result of evaluating the persisted health marker once at process startup.
/// <see cref="ShouldRollBack"/> is true only when the running version has failed to reach a
/// healthy state twice in a row AND a rollback target was recorded for it.
/// </summary>
public sealed record UpdateStartupDecision(bool ShouldRollBack, RollbackTarget? Target);

/// <summary>
/// Persists a tiny JSON health marker so a machine that fails to start after an update can
/// revert to the previous version without operator action. Velopack exposes no crash-loop or
/// rollback mechanism of its own, and "failed start" cannot be detected from inside a crashing
/// process, so the decision is made on the next launch from what was left on disk.
///
/// Lifecycle:
///   - Every successful launch calls <see cref="MarkHealthy"/> once the UI is confirmed working.
///   - Right before applying a verified, downloaded update, the caller records the currently
///     healthy version as the rollback target and marks the incoming version "pending" via
///     <see cref="MarkPendingRestart"/>.
///   - On each startup <see cref="EvaluateStartup"/> gives a pending version exactly one chance,
///     and only reports a rollback on the following startup if that chance never turned healthy.
///
/// This type is pure <c>System.Text.Json</c> file I/O. It has no dependency on WinForms,
/// Velopack, or any other PlanCope type, so it is fully testable on a non-Windows host.
/// </summary>
public sealed class UpdateHealthTracker
{
    private const string HealthyState = "healthy";
    private const string PendingState = "pending";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _markerFilePath;

    /// <summary>
    /// Creates a tracker over an explicit marker path. The caller decides the location (so this
    /// class stays independent of <c>DataDirectoryResolver</c>) and a test can point it at a
    /// temp file.
    /// </summary>
    public UpdateHealthTracker(string markerFilePath)
    {
        _markerFilePath = markerFilePath;
    }

    /// <summary>
    /// Records <paramref name="version"/> as having reached a working state: resets the failure
    /// counter and closes out any pending cycle, clearing the rollback fields.
    ///
    /// The marker's own <c>FileName</c>/<c>Sha256</c>/<c>DownloadUrl</c> are carried over when the
    /// existing marker describes the same version — that is where a pending marker written by
    /// <see cref="MarkPendingRestart"/> left the package info for the version now running. Those
    /// fields describe the version itself (not the rollback), so retaining them is what lets a
    /// later <see cref="MarkPendingRestart"/> point back at this version.
    /// </summary>
    public void MarkHealthy(string version)
    {
        var existing = ReadMarker();
        var sameVersion = existing is not null &&
            string.Equals(existing.Version, version, StringComparison.Ordinal);

        var marker = new Marker
        {
            State = HealthyState,
            Version = version,
            FileName = sameVersion ? existing!.FileName : null,
            Sha256 = sameVersion ? existing!.Sha256 : null,
            DownloadUrl = sameVersion ? existing!.DownloadUrl : null,
            FailedAttempts = 0,
        };

        WriteMarker(marker);
    }

    /// <summary>
    /// Records that a verified, downloaded <paramref name="newVersion"/> is about to be applied.
    /// When the current marker is healthy, its version and package info become the rollback
    /// target (the known-good version we are about to replace). When no marker exists yet, or it
    /// carries no usable version info — e.g. the very first update applied to the originally
    /// installed package, which never went through this tracker — the rollback fields stay null.
    /// Rollback past a version this tracker never itself recorded is not possible; that is an
    /// accepted limitation, not something to work around here.
    /// </summary>
    public void MarkPendingRestart(string newVersion, string newFileName, string newSha256, string newDownloadUrl)
    {
        var existing = ReadMarker();
        var rollbackSource = existing is { State: HealthyState } ? existing : null;

        var marker = new Marker
        {
            State = PendingState,
            Version = newVersion,
            FileName = newFileName,
            Sha256 = newSha256,
            DownloadUrl = newDownloadUrl,
            FailedAttempts = 0,
            RollbackVersion = rollbackSource?.Version,
            RollbackFileName = rollbackSource?.FileName,
            RollbackSha256 = rollbackSource?.Sha256,
            RollbackDownloadUrl = rollbackSource?.DownloadUrl,
        };

        WriteMarker(marker);
    }

    /// <summary>
    /// Evaluates the persisted marker once at startup, before the main window is shown.
    ///
    /// A pending marker for the running version gets one grace startup (the restart into it may
    /// simply not have had a chance to mark healthy yet). Only a second startup that still finds
    /// no healthy mark triggers a rollback, and only when a complete rollback target was
    /// recorded. This method never resets a pending marker after deciding to roll back; the
    /// caller closes the cycle with a later <see cref="MarkHealthy"/> or
    /// <see cref="MarkPendingRestart"/> once it has acted.
    ///
    /// A missing, empty, or corrupt marker is treated as a normal startup and never throws.
    /// </summary>
    public UpdateStartupDecision EvaluateStartup(string currentlyRunningVersion)
    {
        var marker = ReadMarker();

        if (marker is null || !string.Equals(marker.State, PendingState, StringComparison.Ordinal))
        {
            return new UpdateStartupDecision(false, null);
        }

        if (!string.Equals(marker.Version, currentlyRunningVersion, StringComparison.Ordinal))
        {
            MarkHealthy(currentlyRunningVersion);
            return new UpdateStartupDecision(false, null);
        }

        if (marker.FailedAttempts == 0)
        {
            marker.FailedAttempts = 1;
            WriteMarker(marker);
            return new UpdateStartupDecision(false, null);
        }

        var target = BuildTarget(marker);
        return target is null
            ? new UpdateStartupDecision(false, null)
            : new UpdateStartupDecision(true, target);
    }

    private static RollbackTarget? BuildTarget(Marker marker)
    {
        if (string.IsNullOrEmpty(marker.RollbackVersion) ||
            string.IsNullOrEmpty(marker.RollbackFileName) ||
            string.IsNullOrEmpty(marker.RollbackSha256) ||
            string.IsNullOrEmpty(marker.RollbackDownloadUrl))
        {
            return null;
        }

        return new RollbackTarget(
            marker.RollbackVersion,
            marker.RollbackFileName,
            marker.RollbackSha256,
            marker.RollbackDownloadUrl);
    }

    private Marker? ReadMarker()
    {
        try
        {
            if (!File.Exists(_markerFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_markerFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Marker>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private void WriteMarker(Marker marker)
    {
        var json = JsonSerializer.Serialize(marker, SerializerOptions);

        var directory = Path.GetDirectoryName(_markerFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _markerFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _markerFilePath, overwrite: true);
    }

    private sealed class Marker
    {
        public string? State { get; set; }

        public string? Version { get; set; }

        public string? FileName { get; set; }

        public string? Sha256 { get; set; }

        public string? DownloadUrl { get; set; }

        public string? RollbackVersion { get; set; }

        public string? RollbackFileName { get; set; }

        public string? RollbackSha256 { get; set; }

        public string? RollbackDownloadUrl { get; set; }

        public int FailedAttempts { get; set; }
    }
}

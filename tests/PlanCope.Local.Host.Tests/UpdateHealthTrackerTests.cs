using System.Text.Json;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class UpdateHealthTrackerTests
{
    private const string RollbackFileName = "PlanCope-1.1.0-full.nupkg";
    private const string RollbackSha256 = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string RollbackDownloadUrl = "https://feed.example/nodes/stable/1.1.0";
    private const string PendingFileName = "PlanCope-1.2.0-full.nupkg";
    private const string PendingSha256 = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string PendingDownloadUrl = "https://feed.example/nodes/stable/1.2.0";

    [Fact]
    public void EvaluateStartup_NoMarkerFile_ReturnsNoRollback()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        var decision = tracker.EvaluateStartup("1.0.0");

        Assert.False(decision.ShouldRollBack);
        Assert.Null(decision.Target);
    }

    [Fact]
    public void EvaluateStartup_HealthyMarkerForRunningVersion_ReturnsNoRollback()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        tracker.MarkHealthy("1.0.0");

        var decision = tracker.EvaluateStartup("1.0.0");

        Assert.False(decision.ShouldRollBack);
        Assert.Null(decision.Target);
    }

    [Fact]
    public void MarkPendingRestart_AfterHealthyVersion_CarriesRollbackTargetForward()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        // The originally installed version has no package info this tracker ever saw, so the
        // first update records no rollback target.
        tracker.MarkHealthy("1.0.0");
        tracker.MarkPendingRestart("1.1.0", RollbackFileName, RollbackSha256, RollbackDownloadUrl);

        // 1.1.0 then starts cleanly: MarkHealthy retains its package info from the pending marker.
        tracker.MarkHealthy("1.1.0");

        // The next update must remember 1.1.0 as the known-good version to fall back to.
        tracker.MarkPendingRestart("1.2.0", PendingFileName, PendingSha256, PendingDownloadUrl);

        var persisted = ReadMarker(marker.FilePath);

        Assert.Equal("pending", persisted.GetProperty("State").GetString());
        Assert.Equal("1.2.0", persisted.GetProperty("Version").GetString());
        Assert.Equal(PendingFileName, persisted.GetProperty("FileName").GetString());
        Assert.Equal(PendingSha256, persisted.GetProperty("Sha256").GetString());
        Assert.Equal(PendingDownloadUrl, persisted.GetProperty("DownloadUrl").GetString());
        Assert.Equal(0, persisted.GetProperty("FailedAttempts").GetInt32());
        Assert.Equal("1.1.0", persisted.GetProperty("RollbackVersion").GetString());
        Assert.Equal(RollbackFileName, persisted.GetProperty("RollbackFileName").GetString());
        Assert.Equal(RollbackSha256, persisted.GetProperty("RollbackSha256").GetString());
        Assert.Equal(RollbackDownloadUrl, persisted.GetProperty("RollbackDownloadUrl").GetString());
    }

    [Fact]
    public void MarkPendingRestart_WithoutPriorHealthyMarker_LeavesRollbackUnset()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        tracker.MarkPendingRestart("1.1.0", PendingFileName, PendingSha256, PendingDownloadUrl);

        var persisted = ReadMarker(marker.FilePath);
        Assert.Equal(JsonValueKind.Null, persisted.GetProperty("RollbackVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, persisted.GetProperty("RollbackFileName").ValueKind);
        Assert.Equal(JsonValueKind.Null, persisted.GetProperty("RollbackSha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, persisted.GetProperty("RollbackDownloadUrl").ValueKind);

        // Two startups with no healthy mark must NOT report a rollback when there is no target.
        Assert.False(tracker.EvaluateStartup("1.1.0").ShouldRollBack);
        var second = tracker.EvaluateStartup("1.1.0");
        Assert.False(second.ShouldRollBack);
        Assert.Null(second.Target);
    }

    [Fact]
    public void EvaluateStartup_PendingFirstStartup_GivesOneChance_ThenRollsBackOnSecond()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        tracker.MarkHealthy("1.0.0");
        tracker.MarkPendingRestart("1.1.0", RollbackFileName, RollbackSha256, RollbackDownloadUrl);
        tracker.MarkHealthy("1.1.0");
        tracker.MarkPendingRestart("1.2.0", PendingFileName, PendingSha256, PendingDownloadUrl);

        var first = tracker.EvaluateStartup("1.2.0");

        Assert.False(first.ShouldRollBack);
        Assert.Null(first.Target);
        Assert.Equal(1, ReadMarker(marker.FilePath).GetProperty("FailedAttempts").GetInt32());

        var second = tracker.EvaluateStartup("1.2.0");

        Assert.True(second.ShouldRollBack);
        Assert.NotNull(second.Target);
        Assert.Equal("1.1.0", second.Target!.Version);
        Assert.Equal(RollbackFileName, second.Target.FileName);
        Assert.Equal(RollbackSha256, second.Target.Sha256);
        Assert.Equal(RollbackDownloadUrl, second.Target.DownloadUrl);
    }

    [Fact]
    public void EvaluateStartup_MarkerVersionMismatch_ReturnsNoRollbackAndSelfHeals()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        tracker.MarkPendingRestart("1.1.0", PendingFileName, PendingSha256, PendingDownloadUrl);

        var decision = tracker.EvaluateStartup("1.0.0");

        Assert.False(decision.ShouldRollBack);
        Assert.Null(decision.Target);

        var healed = ReadMarker(marker.FilePath);
        Assert.Equal("healthy", healed.GetProperty("State").GetString());
        Assert.Equal("1.0.0", healed.GetProperty("Version").GetString());
        Assert.Equal(0, healed.GetProperty("FailedAttempts").GetInt32());
        Assert.Equal(JsonValueKind.Null, healed.GetProperty("RollbackVersion").ValueKind);
    }

    [Fact]
    public void EvaluateStartup_CorruptOrEmptyMarker_DoesNotThrow_ReturnsSafeDefault()
    {
        using var marker = new TempMarkerFile();
        var tracker = new UpdateHealthTracker(marker.FilePath);

        File.WriteAllBytes(marker.FilePath, new byte[] { 0x7B, 0x00, 0x01, 0xFF, 0xFE, 0x80 });
        var corrupt = tracker.EvaluateStartup("1.0.0");
        Assert.False(corrupt.ShouldRollBack);
        Assert.Null(corrupt.Target);

        File.WriteAllText(marker.FilePath, string.Empty);
        var empty = tracker.EvaluateStartup("1.0.0");
        Assert.False(empty.ShouldRollBack);
        Assert.Null(empty.Target);
    }

    private static JsonElement ReadMarker(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private sealed class TempMarkerFile : IDisposable
    {
        public TempMarkerFile()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plan-cope-health-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            FilePath = System.IO.Path.Combine(Root, "update-health.json");
        }

        public string Root { get; }

        public string FilePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}

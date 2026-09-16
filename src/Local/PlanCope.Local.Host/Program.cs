using Velopack;

namespace PlanCope.Local.Host;

using PlanCope.Local.Host.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        var directories = new DataDirectoryResolver();
        new LegacyDatabaseMigrator(directories).MigrateIfNeeded();

        var healthMarkerPath = Path.Combine(directories.ConfigDirectory, "update-health.json");
        var healthTracker = new UpdateHealthTracker(healthMarkerPath);
        TryAutomaticRollback(healthTracker);

        Application.Run(new MainForm(directories, new ActivationKeyStore(directories), healthTracker));
    }

    // A machine that fails to start twice in a row after an update reverts to the last
    // known-good version automatically, with no operator action. Every failure mode here falls
    // through to a normal launch — a bug in rollback must never be worse than not having
    // rollback at all, so this never throws or blocks Application.Run.
    private static void TryAutomaticRollback(UpdateHealthTracker healthTracker)
    {
        var version = MainForm.GetInstalledAppVersion();
        if (version is null)
        {
            // Not running as an installed app (this dev/build environment) — nothing to evaluate.
            return;
        }

        var decision = healthTracker.EvaluateStartup(version);
        if (!decision.ShouldRollBack || decision.Target is null)
        {
            return;
        }

        var feedUrl = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_FEED_URL");
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            // No feed configured means this app was never receiving gated updates in the first
            // place; there is nothing to roll back against. Proceed to a normal launch.
            return;
        }

        var channel = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_CHANNEL") is { } c && !string.IsNullOrWhiteSpace(c)
            ? c
            : "stable";

        try
        {
            var backend = new VelopackUpdateBackend(feedUrl, channel, () => null);
            // TryRollBack exits the process on success; a false return means the retained local
            // package was missing or failed its checksum, so this falls through to a normal
            // launch on the (still-broken) current version rather than refusing to start at all.
            backend.TryRollBack(decision.Target.Version, decision.Target.FileName, decision.Target.Sha256);
        }
        catch
        {
            // Rollback must never prevent the app from launching.
        }
    }
}

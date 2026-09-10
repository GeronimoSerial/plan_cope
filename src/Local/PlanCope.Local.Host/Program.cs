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
        Application.Run(new MainForm(directories, new ActivationKeyStore(directories)));
    }
}

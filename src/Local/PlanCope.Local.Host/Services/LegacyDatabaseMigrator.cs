namespace PlanCope.Local.Host.Services;

public sealed class LegacyDatabaseMigrator(DataDirectoryResolver directories)
{
    public bool MigrateIfNeeded(string? legacyDirectory = null)
    {
        var destination = directories.DatabasePath;
        if (File.Exists(destination))
        {
            return false;
        }

        var source = Path.Combine(legacyDirectory ?? AppContext.BaseDirectory, "plan-cope-local.db");
        if (!File.Exists(source) || PathsReferToSameFile(source, destination))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
        return true;
    }

    private static bool PathsReferToSameFile(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

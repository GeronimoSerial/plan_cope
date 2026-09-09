namespace PlanCope.Local.Host.Services;

public sealed class DataDirectoryResolver
{
    public DataDirectoryResolver(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(ResolveRoot(rootDirectory));
        DataDirectory = CreateSubdirectory("data");
        AssetsDirectory = CreateSubdirectory("assets");
        ConfigDirectory = CreateSubdirectory("config");
        LogsDirectory = CreateSubdirectory("logs");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string AssetsDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogsDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "plan-cope-local.db");

    private static string ResolveRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return explicitRoot;
        }

        var configuredRoot = Environment.GetEnvironmentVariable("PLANCOPE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return configuredRoot;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlanCope");
    }

    private string CreateSubdirectory(string name)
    {
        var path = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }
}

namespace PlanCope.Local.Api.Services;

public static class LocalClientAppFiles
{
    public static string? FindDistPath()
    {
        var configuredDistPath = Environment.GetEnvironmentVariable("PLANCOPE_CLIENT_APP_DIST");
        var candidates = new[]
        {
            configuredDistPath,
            Path.Combine(AppContext.BaseDirectory, "ClientApp", "dist"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PlanCope.Local.Host", "ClientApp", "dist")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "Local", "PlanCope.Local.Host", "ClientApp", "dist"))
        };

        return candidates
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .FirstOrDefault(path => File.Exists(Path.Combine(path!, "index.html")));
    }

    public static string? FindIndexPath()
    {
        var distPath = FindDistPath();
        return distPath is null ? null : Path.Combine(distPath, "index.html");
    }
}

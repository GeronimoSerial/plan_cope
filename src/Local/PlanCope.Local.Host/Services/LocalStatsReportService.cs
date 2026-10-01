using System.Text;

namespace PlanCope.Local.Host.Services;

internal sealed class LocalStatsReportService(HttpClient httpClient, string reportsDirectory)
{
    internal const int MaximumReportCount = 20;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal string ReportsDirectory { get; } = Path.GetFullPath(reportsDirectory);

    internal async Task<string> SaveReportAsync(
        string apiBaseUrl,
        string cue,
        string? schoolYear,
        string? course,
        string? exam,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            throw new ArgumentException("El CUE es obligatorio.", nameof(cue));
        }

        var query = new List<string> { $"cue={Uri.EscapeDataString(cue)}" };
        AddQuery(query, "schoolYear", schoolYear);
        AddQuery(query, "course", course);
        AddQuery(query, "exam", exam);
        using var response = await httpClient.GetAsync(
            $"{apiBaseUrl.TrimEnd('/')}/api/stats/report.html?{string.Join('&', query)}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        Directory.CreateDirectory(ReportsDirectory);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        var filename = $"informe-{Sanitize(cue)}-{Sanitize(schoolYear)}-{Sanitize(course)}-{Sanitize(exam)}-{timestamp}.html";
        var path = Path.GetFullPath(Path.Combine(ReportsDirectory, filename));
        if (!IsInsideReportsDirectory(ReportsDirectory, path))
        {
            throw new InvalidOperationException("La ruta del informe no es válida.");
        }

        await File.WriteAllBytesAsync(path, html, cancellationToken);
        PruneOldReports();
        return path;
    }

    internal static bool IsInsideReportsDirectory(string reportsDirectory, string path)
    {
        var root = Path.GetFullPath(reportsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, PathComparison)
            && Path.GetExtension(fullPath).Equals(".html", StringComparison.OrdinalIgnoreCase);
    }

    internal static void OpenReport(string reportsDirectory, string path, Action<string> openFile)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInsideReportsDirectory(reportsDirectory, fullPath))
        {
            throw new InvalidOperationException("La ruta del informe no es válida.");
        }

        openFile(fullPath);
    }

    private void PruneOldReports()
    {
        var reports = Directory.EnumerateFiles(ReportsDirectory, "*.html", SearchOption.TopDirectoryOnly)
            .Where(path => IsInsideReportsDirectory(ReportsDirectory, path))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var oldReport in reports.Skip(MaximumReportCount))
        {
            File.Delete(oldReport);
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "todos";
        var safe = new StringBuilder(Math.Min(value.Length, 48));
        foreach (var character in value)
        {
            if (safe.Length >= 48) break;
            safe.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }
        return safe.ToString().Trim('_') is { Length: > 0 } result ? result : "dato";
    }

    private static void AddQuery(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) query.Add($"{key}={Uri.EscapeDataString(value)}");
    }
}

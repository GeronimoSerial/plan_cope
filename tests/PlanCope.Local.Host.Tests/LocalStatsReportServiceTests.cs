using System.Net;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class LocalStatsReportServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "plancope-report-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveReportAsync_sanitizes_filters_and_writes_inside_reports_directory()
    {
        var handler = new CapturingHandler("<html>snapshot</html>");
        var service = new LocalStatsReportService(new HttpClient(handler), root);

        var path = await service.SaveReportAsync("http://127.0.0.1:5055", "12/34", "2026", "6 A", "exam&1");

        Assert.True(LocalStatsReportService.IsInsideReportsDirectory(root, path));
        Assert.Contains("12_34", Path.GetFileName(path));
        Assert.Equal("<html>snapshot</html>", await File.ReadAllTextAsync(path));
        Assert.Equal("cue=12%2F34&schoolYear=2026&course=6%20A&exam=exam%261",
            handler.RequestUri!.GetComponents(UriComponents.Query, UriFormat.UriEscaped));
    }

    [Fact]
    public void IsInsideReportsDirectory_rejects_parent_paths_and_non_html_files()
    {
        Assert.False(LocalStatsReportService.IsInsideReportsDirectory(root, Path.Combine(root, "..", "outside.html")));
        Assert.False(LocalStatsReportService.IsInsideReportsDirectory(root, Path.Combine(root, "report.csv")));
        Assert.True(LocalStatsReportService.IsInsideReportsDirectory(root, Path.Combine(root, "report.HTML")));
    }

    [Fact]
    public void OpenReport_passes_only_validated_local_html_paths_to_the_shell()
    {
        string? openedPath = null;
        var expectedPath = Path.Combine(root, "report.html");

        LocalStatsReportService.OpenReport(root, expectedPath, path => openedPath = path);

        Assert.Equal(Path.GetFullPath(expectedPath), openedPath);
        Assert.Throws<InvalidOperationException>(() =>
            LocalStatsReportService.OpenReport(root, Path.Combine(root, "..", "outside.html"), _ =>
                throw new Xunit.Sdk.XunitException("An outside path must not be opened.")));
    }

    [Fact]
    public async Task SaveReportAsync_keeps_only_the_latest_twenty_reports()
    {
        Directory.CreateDirectory(root);
        for (var index = 0; index < 22; index++)
        {
            var path = Path.Combine(root, $"old-{index:00}.html");
            await File.WriteAllTextAsync(path, "old");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-index - 1));
        }
        var service = new LocalStatsReportService(new HttpClient(new CapturingHandler("new")), root);

        await service.SaveReportAsync("http://127.0.0.1:5055", "123456789", null, null, null);

        Assert.Equal(LocalStatsReportService.MaximumReportCount, Directory.GetFiles(root, "*.html").Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}

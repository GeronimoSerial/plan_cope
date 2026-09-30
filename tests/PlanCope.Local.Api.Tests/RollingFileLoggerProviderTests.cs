using Microsoft.Extensions.Logging;
using PlanCope.Local.Api.Services;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class RollingFileLoggerProviderTests
{
    [Fact]
    public void Writes_warning_and_error_entries_and_ignores_lower_levels()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"plancope-logs-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "local-api.log");
        try
        {
            using (var provider = new RollingFileLoggerProvider(path))
            {
                var logger = provider.CreateLogger("test-category");
                logger.LogInformation("not persisted");
                logger.LogWarning("field warning");
                logger.LogError(new InvalidOperationException("details"), "field error");
            }

            var content = File.ReadAllText(path);
            Assert.Contains("[Warning] test-category", content);
            Assert.Contains("field warning", content);
            Assert.Contains("[Error] test-category", content);
            Assert.Contains("InvalidOperationException: details", content);
            Assert.DoesNotContain("not persisted", content);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

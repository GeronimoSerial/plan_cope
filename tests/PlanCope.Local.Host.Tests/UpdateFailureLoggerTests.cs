using System.Net;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class UpdateFailureLoggerTests
{
    [Fact]
    public void Log_WritesFailureDetailsAndRedactsTokensAndUrlQueries()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), $"plancope-update-log-{Guid.NewGuid():N}");
        const string token = "secret-node-token";

        try
        {
            UpdateFailureLogger.Log(
                logsDirectory,
                "https://central.example/api/updates/stable?token=feed-secret",
                token,
                "check",
                new HttpRequestException($"Request to https://central.example/feed?access_token={token} failed", null, HttpStatusCode.BadGateway));

            var log = File.ReadAllText(Path.Combine(logsDirectory, "local-host.log"));
            Assert.Contains("Update check failed: HttpRequestException: HTTP 502 (BadGateway).", log);
            Assert.Contains("https://central.example/feed", log);
            Assert.Contains("https://central.example/api/updates/stable", log);
            Assert.DoesNotContain(token, log);
            Assert.DoesNotContain("feed-secret", log);
            Assert.DoesNotContain("access_token=", log);
        }
        finally
        {
            if (Directory.Exists(logsDirectory)) Directory.Delete(logsDirectory, recursive: true);
        }
    }
}

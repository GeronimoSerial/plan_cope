using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class DownloadControllerTests
{
    // Tradeoff (reported to the coordinator): the repo's test conventions are direct
    // controller construction (see AuthControllerTests / RostersAuthorizationTests) with no
    // WebApplicationFactory/TestServer anywhere. The actual 401 for the unauthenticated
    // request is produced by the JWT middleware in the real pipeline, which those tests never
    // exercise; the [Authorize] attribute check below is the proportionate proxy for it.

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var authorize = typeof(DownloadController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
    }

    [Fact]
    public async Task GetLatestInstaller_WithNotConfiguredStorage_Returns503()
    {
        var controller = new DownloadController(
            new NotConfiguredInstallerStorage(NullLogger<NotConfiguredInstallerStorage>.Instance));

        var result = await controller.GetLatestInstaller("stable", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
        var body = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadLatestInstaller_WithNotConfiguredStorage_Returns503()
    {
        var controller = new DownloadController(
            new NotConfiguredInstallerStorage(NullLogger<NotConfiguredInstallerStorage>.Instance));

        var result = await controller.DownloadLatestInstaller("stable", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    [Fact]
    public async Task DownloadLatestInstaller_WithEncOnlyRelease_Returns404()
    {
        var handler = new FakeGitHubHandler(
            releasesJson: """
                [{
                  "tag_name": "roster-2026",
                  "prerelease": false,
                  "published_at": "2026-09-10T17:21:41Z",
                  "assets": [{ "name": "roster-2026-upload.enc", "url": "https://api.github.com/assets/1" }]
                }]
                """,
            assetBytes: []);
        var storage = CreateGitHubStorage(handler);
        var controller = new DownloadController(storage);

        var result = await controller.DownloadLatestInstaller("stable", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadLatestInstaller_WithInstallerAsset_StreamsBytesWithHeaders()
    {
        var handler = new FakeGitHubHandler(
            releasesJson: """
                [{
                  "tag_name": "1.2.3",
                  "prerelease": false,
                  "published_at": "2026-09-11T10:00:00Z",
                  "assets": [{ "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/2" }]
                }]
                """,
            assetBytes: "fake-installer-bytes"u8.ToArray());
        var storage = CreateGitHubStorage(handler);
        var controller = new DownloadController(storage);
        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.DownloadLatestInstaller("stable", CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("attachment; filename=\"PlanCope.setup.exe\"", httpContext.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("fake-installer-bytes".Length, httpContext.Response.ContentLength);
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        Assert.Equal("fake-installer-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task DownloadLatestInstaller_NeverServesTheEncryptedRosterEvenIfListedFirst()
    {
        var handler = new FakeGitHubHandler(
            releasesJson: """
                [{
                  "tag_name": "1.2.3",
                  "prerelease": false,
                  "published_at": "2026-09-11T10:00:00Z",
                  "assets": [
                    { "name": "roster-2026-upload.enc", "url": "https://api.github.com/assets/3" },
                    { "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/4" }
                  ]
                }]
                """,
            assetBytes: "fake-installer-bytes"u8.ToArray());
        var storage = CreateGitHubStorage(handler);
        var controller = new DownloadController(storage);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.DownloadLatestInstaller("stable", CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("attachment; filename=\"PlanCope.setup.exe\"", httpContext.Response.Headers.ContentDisposition.ToString());
        Assert.DoesNotContain("roster", httpContext.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubReleaseInstallerStorage CreateGitHubStorage(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        return new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance, new MemoryCache(new MemoryCacheOptions()));
    }

    private sealed class FakeGitHubHandler(string releasesJson, byte[] assetBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/releases"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releasesJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(assetBytes)
            });
        }
    }
}

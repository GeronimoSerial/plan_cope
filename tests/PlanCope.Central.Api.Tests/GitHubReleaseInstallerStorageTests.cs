using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Asset selection, against a fake GitHub Releases payload. These tests exist because the
/// private release repository is not guaranteed to contain only installers — at the time this
/// was written the real one held a single release, tag "roster-2026", whose only asset was the
/// encrypted provincial roster.
/// </summary>
public sealed class GitHubReleaseInstallerStorageTests
{
    private static GitHubReleaseInstallerStorage CreateStorage(string releasesJson)
    {
        var handler = new StubHandler(releasesJson);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        return new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance);
    }

    [Fact]
    public async Task A_release_whose_only_asset_is_the_encrypted_roster_yields_no_installer()
    {
        // This is the shape of the real release that existed in the private repo. Before the
        // allow-list, this returned the .enc bundle as the desktop installer.
        var storage = CreateStorage("""
            [{
              "tag_name": "roster-2026",
              "prerelease": false,
              "published_at": "2026-09-10T17:21:41Z",
              "assets": [{ "name": "roster-2026-upload.enc", "url": "https://api.github.com/assets/1" }]
            }]
            """);

        var installer = await storage.GetLatestAsync("stable", CancellationToken.None);

        Assert.Null(installer);
    }

    [Fact]
    public async Task An_installer_asset_is_selected_and_carries_the_release_tag_as_its_version()
    {
        var storage = CreateStorage("""
            [{
              "tag_name": "1.2.3",
              "prerelease": false,
              "published_at": "2026-09-11T10:00:00Z",
              "assets": [{ "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/2" }]
            }]
            """);

        var installer = await storage.GetLatestAsync("stable", CancellationToken.None);

        Assert.NotNull(installer);
        Assert.Equal("1.2.3", installer!.Version);
        Assert.Equal("/api/downloads/installer/file?channel=stable", installer.DownloadUrl.ToString());
    }

    [Fact]
    public async Task A_non_installer_asset_never_shadows_the_installer_in_the_same_release()
    {
        // Ordering matters: the roster bundle is listed first, exactly the case the old
        // "first asset that isn't source code" rule got wrong.
        var storage = CreateStorage("""
            [{
              "tag_name": "1.2.3",
              "prerelease": false,
              "published_at": "2026-09-11T10:00:00Z",
              "assets": [
                { "name": "roster-2026-upload.enc", "url": "https://api.github.com/assets/3" },
                { "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/4" }
              ]
            }]
            """);

        var installer = await storage.GetLatestAsync("stable", CancellationToken.None);

        Assert.NotNull(installer);
        Assert.Equal("/api/downloads/installer/file?channel=stable", installer!.DownloadUrl.ToString());
    }

    [Fact]
    public async Task GetLatestDownloadAsync_StreamsTheInstallerBytes()
    {
        var handler = new DownloadStubHandler(
            releasesJson: """
                [{
                  "tag_name": "1.2.3",
                  "prerelease": false,
                  "published_at": "2026-09-11T10:00:00Z",
                  "assets": [{ "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/2" }]
                }]
                """,
            assetBytes: "fake-installer-bytes"u8.ToArray());
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        var storage = new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance);

        using var download = await storage.GetLatestDownloadAsync("stable", CancellationToken.None);

        Assert.NotNull(download);
        Assert.Equal("PlanCope.setup.exe", download!.FileName);
        using var reader = new StreamReader(download.Content);
        Assert.Equal("fake-installer-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task GetLatestDownloadAsync_WithEncOnlyRelease_ReturnsNull()
    {
        var handler = new DownloadStubHandler(
            releasesJson: """
                [{
                  "tag_name": "roster-2026",
                  "prerelease": false,
                  "published_at": "2026-09-10T17:21:41Z",
                  "assets": [{ "name": "roster-2026-upload.enc", "url": "https://api.github.com/assets/1" }]
                }]
                """,
            assetBytes: []);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        var storage = new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance);

        var download = await storage.GetLatestDownloadAsync("stable", CancellationToken.None);

        Assert.Null(download);
    }

    [Fact]
    public async Task GetLatestAsync_WithoutUserAgentHeader_GitHubRejectsWith403()
    {
        var handler = new UserAgentGatedHandler(releasesJson: """
            [{
              "tag_name": "1.2.3",
              "prerelease": false,
              "published_at": "2026-09-11T10:00:00Z",
              "assets": [{ "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/2" }]
            }]
            """);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        // Deliberately NOT calling GitHubHttpClientDefaults.Apply(client) — this reproduces the
        // production bug: no User-Agent means GitHub's API answers 403 regardless of a valid token.
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        var storage = new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance);

        var installer = await storage.GetLatestAsync("stable", CancellationToken.None);

        Assert.Null(installer);
    }

    [Fact]
    public async Task GetLatestAsync_WithUserAgentHeaderConfigured_Succeeds()
    {
        var handler = new UserAgentGatedHandler(releasesJson: """
            [{
              "tag_name": "1.2.3",
              "prerelease": false,
              "published_at": "2026-09-11T10:00:00Z",
              "assets": [{ "name": "PlanCope.setup.exe", "url": "https://api.github.com/assets/2" }]
            }]
            """);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        GitHubHttpClientDefaults.Apply(client);
        var options = Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "t" });
        var storage = new GitHubReleaseInstallerStorage(client, options, NullLogger<GitHubReleaseInstallerStorage>.Instance);

        var installer = await storage.GetLatestAsync("stable", CancellationToken.None);

        Assert.NotNull(installer);
        Assert.Equal("1.2.3", installer!.Version);
    }

    // Mirrors GitHub's real behaviour: any request with no User-Agent header is rejected with 403,
    // independent of the token being valid. The other fakes in this file (StubHandler,
    // DownloadStubHandler) accept any request regardless of headers, which is exactly why the
    // missing User-Agent bug shipped to production undetected.
    private sealed class UserAgentGatedHandler(string releasesJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.UserAgent.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{"message":"Request forbidden by administrative rules."}""", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releasesJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class DownloadStubHandler(string releasesJson, byte[] assetBytes) : HttpMessageHandler
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

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}

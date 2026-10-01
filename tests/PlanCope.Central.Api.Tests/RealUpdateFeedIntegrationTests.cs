using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RealUpdateFeedIntegrationTests
{
    private const string Version = "1.0.15";
    private const string FileName = "PlanCope.Local.Host-1.0.15-stable-full.nupkg";

    [Fact]
    public async Task RealVpkFeedFlowsThroughStorageControllersAndVelopackWireFormat()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "releases.stable.json");
        var fixture = await File.ReadAllTextAsync(fixturePath);
        var handler = new RealUpdateFeedHandler(fixture);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var storage = new GitHubReleaseInstallerStorage(
            client,
            Options.Create(new InstallerStorageOptions { Repo = "acme/installers", Token = "test-token" }),
            NullLogger<GitHubReleaseInstallerStorage>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        var feed = await storage.GetUpdateReleaseFeedAsync("stable", CancellationToken.None);
        Assert.NotNull(feed);
        Assert.Equal(Version, feed!.LatestVersion);
        var parsedAsset = Assert.Single(feed.Assets);
        Assert.Equal(1, parsedAsset.Type);
        Assert.Equal(FileName, parsedAsset.FileName);
        Assert.Equal(88184691, parsedAsset.Size);

        var gate = new AllowReleaseGate();
        using var dbContext = new PlanCopeDbContext(new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var updates = new UpdatesController(gate, storage, dbContext) { ControllerContext = NewControllerContext() };
        var feedAction = await updates.Releases("stable", "stable", null, "1.0.0", CancellationToken.None);
        var feedResponse = Assert.IsType<ContentResult>(feedAction);
        Assert.Equal(StatusCodes.Status200OK, feedResponse.StatusCode);
        using var json = JsonDocument.Parse(feedResponse.Content!);
        var jsonAsset = Assert.Single(json.RootElement.GetProperty("Assets").EnumerateArray());
        Assert.Equal(JsonValueKind.Number, jsonAsset.GetProperty("Type").ValueKind);
        Assert.Equal(1, jsonAsset.GetProperty("Type").GetInt32());
        Assert.Equal(FileName, jsonAsset.GetProperty("FileName").GetString());
        var velopackFeed = Velopack.VelopackAssetFeed.FromJson(feedResponse.Content!);
        var velopackAsset = Assert.Single(velopackFeed.Assets);
        Assert.Equal(Velopack.VelopackAssetType.Full, velopackAsset.Type);
        Assert.Equal(FileName, velopackAsset.FileName);

        var packages = new UpdatePackagesController(storage, gate) { ControllerContext = NewControllerContext() };
        var packageResponse = await packages.Download("stable", FileName, CancellationToken.None);
        Assert.IsType<EmptyResult>(packageResponse);
        Assert.Equal("package-bytes", Encoding.UTF8.GetString(((MemoryStream)packages.Response.Body).ToArray()));
        Assert.Equal(FileName, handler.RequestedPackageName);
    }

    private static ControllerContext NewControllerContext()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("token_type", "node_access"), new Claim("node_id", "node-1")], "Test"))
        };
        context.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = context };
    }

    private sealed class AllowReleaseGate : IReleaseGateService
    {
        public Task<ReleaseGateDecision> ResolveAsync(string nodeId, string currentVersion, string channel, string? latestPublishedVersion, CancellationToken cancellationToken)
            => Task.FromResult(new ReleaseGateDecision(true, latestPublishedVersion));
    }

    private sealed class RealUpdateFeedHandler(string feedJson) : HttpMessageHandler
    {
        public string? RequestedPackageName { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    [{"tag_name":"1.0.15","draft":false,"prerelease":false,"published_at":"2026-10-01T00:00:00Z","assets":[{"name":"releases.stable.json","url":"https://api.github.com/assets/stable-feed"}]}]
                    """);
            }
            if (path.EndsWith("/stable-feed", StringComparison.Ordinal))
            {
                return JsonResponse(feedJson);
            }
            if (path.EndsWith("/releases/tags/1.0.15", StringComparison.Ordinal))
            {
                RequestedPackageName = FileName;
                return JsonResponse("""
                    {"tag_name":"1.0.15","draft":false,"prerelease":false,"assets":[{"name":"PlanCope.Local.Host-1.0.15-stable-full.nupkg","url":"https://api.github.com/assets/package"}]}
                    """);
            }
            if (path.EndsWith("/package", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("package-bytes"u8.ToArray())
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> JsonResponse(string content) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
    }
}

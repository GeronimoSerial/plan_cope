using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class UpdatePackagesControllerTests
{
    [Fact]
    public void Download_RequiresAuthentication() =>
        Assert.NotNull(typeof(UpdatePackagesController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());

    [Fact]
    public async Task Download_RequiresNodeAccessToken()
    {
        var storage = new StubInstallerStorage();
        var controller = CreateController(storage, new StubReleaseGateService(), tokenType: "access", nodeId: "node-1");

        var result = await controller.Download("PlanCope.Local.Host-2.0.0-full.nupkg", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(storage.RequestedAssetName);
    }

    [Theory]
    [InlineData("../package.nupkg")]
    [InlineData("package.exe")]
    [InlineData("OtherPackage-2.0.0-full.nupkg")]
    public async Task Download_RejectsPathsAndNonPackages(string fileName)
    {
        var storage = new StubInstallerStorage();
        var controller = CreateController(storage, new StubReleaseGateService(), tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download(fileName, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        Assert.Null(storage.RequestedAssetName);
    }

    [Fact]
    public async Task Download_StreamsNamedPackageThroughConfiguredStorage()
    {
        var storage = CreateConfiguredStorage();
        var gate = new StubReleaseGateService { Decision = new ReleaseGateDecision(true, "2.0.0") };
        var controller = CreateController(storage, gate, tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download("PlanCope.Local.Host-2.0.0-full.nupkg", CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("PlanCope.Local.Host-2.0.0-full.nupkg", storage.RequestedAssetName);
        Assert.Equal("package-bytes", System.Text.Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray()));
        Assert.Equal("application/octet-stream", controller.Response.ContentType);
        Assert.Equal(13, controller.Response.ContentLength);
        Assert.Equal("stable", gate.RequestedChannel);
        Assert.Equal("", gate.RequestedCurrentVersion);
    }

    [Fact]
    public async Task Download_DeniesPackageWhenGateTargetsAnotherVersion()
    {
        var storage = CreateConfiguredStorage();
        var gate = new StubReleaseGateService { Decision = new ReleaseGateDecision(true, "3.0.0") };
        var controller = CreateController(storage, gate, tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download("PlanCope.Local.Host-2.0.0-full.nupkg", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(storage.RequestedAssetName);
    }

    [Fact]
    public async Task Download_DeniesPackageWhenNodeIsOutsideRollout()
    {
        var storage = CreateConfiguredStorage();
        var gate = new StubReleaseGateService { Decision = ReleaseGateDecision.None };
        var controller = CreateController(storage, gate, tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download("PlanCope.Local.Host-2.0.0-full.nupkg", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Null(storage.RequestedAssetName);
    }

    private static UpdatePackagesController CreateController(StubInstallerStorage storage, StubReleaseGateService gate, string tokenType, string nodeId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("token_type", tokenType), new Claim("node_id", nodeId)], "Test"))
        };
        context.Response.Body = new MemoryStream();
        return new UpdatePackagesController(storage, gate) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static StubInstallerStorage CreateConfiguredStorage() => new()
    {
        IsConfigured = true,
        Feed = new UpdateReleaseFeed("stable", "2.0.0", [new UpdateReleaseAsset(
            "PlanCope.Local.Host", "2.0.0", 1, "PlanCope.Local.Host-2.0.0-full.nupkg", string.Empty,
            new string('b', 64), 13, null, null)])
    };

    private sealed class StubInstallerStorage : IInstallerStorage
    {
        public bool IsConfigured { get; set; }
        public UpdateReleaseFeed? Feed { get; set; }
        public string? RequestedAssetName { get; private set; }
        public Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken) => Task.FromResult<InstallerReference?>(null);
        public Task<InstallerDownload?> GetLatestDownloadAsync(string channel, CancellationToken cancellationToken) => Task.FromResult<InstallerDownload?>(null);
        public Task<UpdateReleaseFeed?> GetUpdateReleaseFeedAsync(string channel, CancellationToken cancellationToken) => Task.FromResult(Feed);
        public Task<InstallerDownload?> GetUpdatePackageDownloadAsync(string channel, string version, string fileName, CancellationToken cancellationToken)
        {
            RequestedAssetName = fileName;
            if (!IsConfigured) return Task.FromResult<InstallerDownload?>(null);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return Task.FromResult<InstallerDownload?>(new InstallerDownload(
                response,
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes("package-bytes")),
                "application/octet-stream",
                fileName,
                13));
        }
    }

    private sealed class StubReleaseGateService : IReleaseGateService
    {
        public ReleaseGateDecision Decision { get; set; } = ReleaseGateDecision.None;
        public string? RequestedCurrentVersion { get; private set; }
        public string? RequestedChannel { get; private set; }
        public Task<ReleaseGateDecision> ResolveAsync(string nodeId, string currentVersion, string channel, string? latestPublishedVersion, CancellationToken cancellationToken)
        {
            RequestedCurrentVersion = currentVersion;
            RequestedChannel = channel;
            return Task.FromResult(Decision);
        }
    }
}

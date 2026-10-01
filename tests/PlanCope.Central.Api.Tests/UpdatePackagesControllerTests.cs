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
        var controller = CreateController(storage, tokenType: "access", nodeId: "node-1");

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
        var controller = CreateController(storage, tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download(fileName, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
        Assert.Null(storage.RequestedAssetName);
    }

    [Fact]
    public async Task Download_StreamsNamedPackageThroughConfiguredStorage()
    {
        var storage = new StubInstallerStorage { IsConfigured = true };
        var controller = CreateController(storage, tokenType: "node_access", nodeId: "node-1");

        var result = await controller.Download("PlanCope.Local.Host-2.0.0-full.nupkg", CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("PlanCope.Local.Host-2.0.0-full.nupkg", storage.RequestedAssetName);
        Assert.Equal("package-bytes", System.Text.Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray()));
        Assert.Equal("application/octet-stream", controller.Response.ContentType);
    }

    private static UpdatePackagesController CreateController(StubInstallerStorage storage, string tokenType, string nodeId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("token_type", tokenType), new Claim("node_id", nodeId)], "Test"))
        };
        context.Response.Body = new MemoryStream();
        return new UpdatePackagesController(storage) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private sealed class StubInstallerStorage : IInstallerStorage
    {
        public bool IsConfigured { get; set; }
        public string? RequestedAssetName { get; private set; }
        public Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken) => Task.FromResult<InstallerReference?>(null);
        public Task<InstallerDownload?> GetLatestDownloadAsync(string channel, CancellationToken cancellationToken) => Task.FromResult<InstallerDownload?>(null);
        public Task<InstallerDownload?> GetAssetDownloadAsync(string assetName, CancellationToken cancellationToken)
        {
            RequestedAssetName = assetName;
            if (!IsConfigured) return Task.FromResult<InstallerDownload?>(null);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return Task.FromResult<InstallerDownload?>(new InstallerDownload(
                response,
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes("package-bytes")),
                "application/octet-stream",
                assetName,
                13));
        }
    }
}

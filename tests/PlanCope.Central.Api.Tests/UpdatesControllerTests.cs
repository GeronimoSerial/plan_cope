using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class UpdatesControllerTests
{
    // Harness: the repo's controller-test convention is direct construction (see
    // DownloadControllerTests / AuthControllerTests) — no WebApplicationFactory/TestServer exists
    // in this project. The [Authorize] attribute is the proxy for the middleware-level 401 (same
    // tradeoff documented in DownloadControllerTests); the in-action 403 claim checks are
    // exercised directly by supplying principals with missing/wrong claims.

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var authorize = typeof(UpdatesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
    }

    [Fact]
    public async Task Releases_WithWrongTokenType_Returns403AndNeverCallsGate()
    {
        var gate = new StubReleaseGateService();
        var controller = CreateController(gate, NodePrincipal(tokenType: "access", nodeId: "node-1"));

        var result = await controller.Releases("stable", "PlanCope.Local.Host", "1.0.0", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(gate.WasCalled);
    }

    [Fact]
    public async Task Releases_WithMissingTokenType_Returns403AndNeverCallsGate()
    {
        var gate = new StubReleaseGateService();
        var controller = CreateController(gate, NodePrincipal(tokenType: null, nodeId: "node-1"));

        var result = await controller.Releases("stable", "PlanCope.Local.Host", "1.0.0", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(gate.WasCalled);
    }

    [Fact]
    public async Task Releases_WithMissingNodeId_Returns403AndNeverCallsGate()
    {
        var gate = new StubReleaseGateService();
        var controller = CreateController(gate, NodePrincipal(tokenType: "node_access", nodeId: null));

        var result = await controller.Releases("stable", "PlanCope.Local.Host", "1.0.0", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(gate.WasCalled);
    }

    [Fact]
    public async Task Releases_WhenGateOffersNothing_Returns200WithEmptyFeed()
    {
        var gate = new StubReleaseGateService { Decision = ReleaseGateDecision.None };
        var controller = CreateController(gate, NodePrincipal(tokenType: "node_access", nodeId: "node-1"));

        var result = await controller.Releases("stable", "PlanCope.Local.Host", "1.0.0", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode!.Value);
        Assert.Equal("application/json", content.ContentType);
        Assert.Equal("""{"Assets":[]}""", content.Content);

        Assert.True(gate.WasCalled);
        Assert.Equal("node-1", gate.RequestedNodeId);
        Assert.Equal("1.0.0", gate.RequestedCurrentVersion);
        Assert.Equal("stable", gate.RequestedChannel);
    }

    [Fact]
    public async Task Releases_WhenGateOffersUpdate_Returns200WithSingleAssetFeedAndPlainStringVersion()
    {
        var decision = new ReleaseGateDecision(
            MayInstall: true,
            TargetVersion: "1.4.0",
            DownloadUrl: "https://downloads.example.test/stable/PlanCope-1.4.0-win-x64.zip",
            Sha256: "abcd1234");
        var gate = new StubReleaseGateService { Decision = decision };
        var controller = CreateController(gate, NodePrincipal(tokenType: "node_access", nodeId: "node-1"));

        var result = await controller.Releases("stable", "PlanCope.Local.Host", "1.0.0", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode!.Value);
        Assert.Equal("application/json", content.ContentType);

        // Exact wire shape verified by round-trip through Velopack.VelopackAssetFeed.FromJson in
        // B7-PROGRESS (against the pinned 0.0.1251). Asserting the raw text — not just a
        // deserialized round-trip — catches a nested-object "Version" regression, which would
        // still deserialize into some C# object.
        const string expected =
            """{"Assets":[{"PackageId":"PlanCope.Local.Host","Version":"1.4.0","Type":1,"FileName":"PlanCope-1.4.0-win-x64.zip","SHA1":"","SHA256":"abcd1234","Size":0,"NotesMarkdown":null,"NotesHTML":null}]}""";
        Assert.Equal(expected, content.Content);

        using var document = JsonDocument.Parse(content.Content!);
        var asset = document.RootElement.GetProperty("Assets")[0];
        var version = asset.GetProperty("Version");
        Assert.Equal(JsonValueKind.String, version.ValueKind);
        Assert.Equal("1.4.0", version.GetString());
        Assert.Equal(JsonValueKind.Number, asset.GetProperty("Type").ValueKind);
        Assert.Equal(1, asset.GetProperty("Type").GetInt32());
        Assert.Equal("PlanCope-1.4.0-win-x64.zip", asset.GetProperty("FileName").GetString());
        Assert.Equal("abcd1234", asset.GetProperty("SHA256").GetString());
        Assert.Equal(string.Empty, asset.GetProperty("SHA1").GetString());
        Assert.Equal(0, asset.GetProperty("Size").GetInt64());
        Assert.Equal(JsonValueKind.Null, asset.GetProperty("NotesMarkdown").ValueKind);
        Assert.Equal(JsonValueKind.Null, asset.GetProperty("NotesHTML").ValueKind);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("Assets").ValueKind);
        Assert.Single(document.RootElement.GetProperty("Assets").EnumerateArray());
    }

    [Fact]
    public async Task Releases_WithoutLocalVersion_PassesEmptyStringToGate()
    {
        var gate = new StubReleaseGateService { Decision = ReleaseGateDecision.None };
        var controller = CreateController(gate, NodePrincipal(tokenType: "node_access", nodeId: "node-1"));

        await controller.Releases("stable", "PlanCope.Local.Host", null, CancellationToken.None);

        Assert.Equal(string.Empty, gate.RequestedCurrentVersion);
    }

    private static UpdatesController CreateController(IReleaseGateService gate, ClaimsPrincipal user)
    {
        return new UpdatesController(gate)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static ClaimsPrincipal NodePrincipal(string? tokenType, string? nodeId)
    {
        var claims = new List<Claim>();
        if (tokenType is not null)
        {
            claims.Add(new Claim("token_type", tokenType));
        }

        if (nodeId is not null)
        {
            claims.Add(new Claim("node_id", nodeId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class StubReleaseGateService : IReleaseGateService
    {
        public ReleaseGateDecision Decision { get; set; } = ReleaseGateDecision.None;

        public bool WasCalled { get; private set; }

        public string? RequestedNodeId { get; private set; }

        public string? RequestedCurrentVersion { get; private set; }

        public string? RequestedChannel { get; private set; }

        public Task<ReleaseGateDecision> ResolveAsync(string nodeId, string currentVersion, string channel, CancellationToken cancellationToken)
        {
            WasCalled = true;
            RequestedNodeId = nodeId;
            RequestedCurrentVersion = currentVersion;
            RequestedChannel = channel;
            return Task.FromResult(Decision);
        }
    }
}
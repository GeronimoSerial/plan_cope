using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Serves the Velopack client release feed for a registered node. The wire protocol is fixed by
/// Velopack's <c>SimpleWebSource</c> (route <c>releases.{channel}.json</c>, PascalCase
/// single-asset JSON) and was verified by round-trip against the pinned Velopack 0.0.1251 — see
/// _briefs/B7-PROGRESS.md. This is D6's enforcement point: only a node-access token may read the
/// feed, so the <c>token_type == node_access</c> claim is checked in addition to <c>[Authorize]</c>
/// — narrower than <c>SyncController</c>'s bare <c>[Authorize]</c>, on purpose.
/// </summary>
[ApiController]
[Authorize]
[Route("api/updates")]
public sealed class UpdatesController(IReleaseGateService releaseGate) : ControllerBase
{
    private const string TokenTypeClaim = "token_type";
    private const string NodeAccessTokenType = "node_access";
    private const string NodeIdClaim = "node_id";
    private const string PackageId = "PlanCope.Local.Host";

    [HttpGet("releases.{channel}.json")]
    public async Task<IActionResult> Releases(
        string channel,
        [FromQuery] string? id,
        [FromQuery] string? localVersion,
        CancellationToken cancellationToken)
    {
        // Velopack's client never sends a node id (confirmed in B7-PROGRESS), so the caller is
        // resolved from the validated JWT's node_id claim — never from a query parameter.
        var tokenType = User.FindFirst(TokenTypeClaim)?.Value;
        var nodeId = User.FindFirst(NodeIdClaim)?.Value;

        // D6 gate: a valid user/operator bearer token (token_type != node_access) must not be able
        // to list or fetch update packages. 403, not 401: the token is valid, just not privileged
        // for this endpoint.
        if (!string.Equals(tokenType, NodeAccessTokenType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(nodeId))
        {
            return Forbid();
        }

        var decision = await releaseGate.ResolveAsync(nodeId, localVersion ?? string.Empty, channel, cancellationToken);

        if (!decision.MayInstall)
        {
            // An empty asset list is Velopack's "no update available". NOT a 404, which would make
            // UpdateManager.CheckForUpdatesAsync throw instead of returning null cleanly.
            return JsonBody("""{"Assets":[]}""");
        }

        var feed = new ReleaseFeedDto(new[]
        {
            new ReleaseAssetDto(
                PackageId,
                decision.TargetVersion ?? string.Empty,
                Type: 1,
                FileName: LastPathSegment(decision.DownloadUrl),
                SHA1: string.Empty,
                SHA256: decision.Sha256 ?? string.Empty,
                Size: 0,
                NotesMarkdown: null,
                NotesHTML: null)
        });

        return JsonBody(JsonSerializer.Serialize(feed));
    }

    private static ContentResult JsonBody(string json) => new()
    {
        Content = json,
        ContentType = "application/json",
        StatusCode = StatusCodes.Status200OK
    };

    private static string LastPathSegment(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return string.Empty;
        }

        var trimmed = downloadUrl.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}

/// <summary>
/// Plain DTO mirroring Velopack's <c>VelopackAssetFeed</c> wire shape. <c>Version</c> must be a
/// plain string — Velopack reads it as a semver string, and serializing a <c>SemanticVersion</c>
/// object directly would emit the wrong nested-object shape.
/// </summary>
public sealed record ReleaseFeedDto(ReleaseAssetDto[] Assets);

public sealed record ReleaseAssetDto(
    string PackageId,
    string Version,
    int Type,
    string FileName,
    string SHA1,
    string SHA256,
    long Size,
    string? NotesMarkdown,
    string? NotesHTML);
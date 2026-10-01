using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/updates")]
public sealed class UpdatePackagesController(IInstallerStorage installerStorage, IReleaseGateService releaseGate) : ControllerBase
{
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        if (!NodeAccessAuth.TryGetNodeId(User, out var nodeId)) return Forbid();
        if (!fileName.StartsWith("PlanCope.Local.Host-", StringComparison.Ordinal) ||
            !(fileName.EndsWith("-full.nupkg", StringComparison.OrdinalIgnoreCase) ||
              fileName.EndsWith("-delta.nupkg", StringComparison.OrdinalIgnoreCase)) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/') || fileName.Contains('\\'))
        {
            return BadRequest();
        }

        if (!installerStorage.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        foreach (var channel in new[] { "stable", "beta" })
        {
            var feed = await installerStorage.GetUpdateReleaseFeedAsync(channel, cancellationToken).ConfigureAwait(false);
            var asset = feed?.Assets.FirstOrDefault(candidate =>
                candidate.PackageId == "PlanCope.Local.Host" && candidate.Type == 1 && candidate.FileName == fileName);
            if (feed is null || asset is null) continue;

            // The asset route has no client version in Velopack's URL. Resolve the channel's
            // current gate target with an empty installed version, then permit only that exact
            // package version. This repeats the rollout decision made when the feed was built.
            var decision = await releaseGate.ResolveAsync(nodeId, string.Empty, channel, feed.LatestVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!decision.MayInstall || !string.Equals(decision.TargetVersion, asset.Version, StringComparison.Ordinal)) continue;

            var download = await installerStorage.GetUpdatePackageDownloadAsync(channel, asset.Version, fileName, cancellationToken)
                .ConfigureAwait(false);
            if (download is null) return NotFound();

            using (download)
            {
                Response.ContentType = download.ContentType;
                Response.Headers.ContentDisposition = $"attachment; filename=\"{download.FileName}\"";
                Response.ContentLength = asset.Size;
                await download.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
            }
            return new EmptyResult();
        }

        return NotFound();
    }
}

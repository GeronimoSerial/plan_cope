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
        var (channel, packageFileName) = fileName.StartsWith("stable__", StringComparison.Ordinal)
            ? ("stable", fileName[8..])
            : fileName.StartsWith("beta__", StringComparison.Ordinal)
                ? ("beta", fileName[6..])
                : (string.Empty, string.Empty);
        if (string.IsNullOrEmpty(channel) ||
            !packageFileName.StartsWith("PlanCope.Local.Host-", StringComparison.Ordinal) ||
            !(packageFileName.EndsWith("-full.nupkg", StringComparison.OrdinalIgnoreCase) ||
              packageFileName.EndsWith("-delta.nupkg", StringComparison.OrdinalIgnoreCase)) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/') || fileName.Contains('\\'))
        {
            return BadRequest();
        }

        if (!installerStorage.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var releaseFeed = await installerStorage.GetUpdateReleaseFeedAsync(channel, cancellationToken).ConfigureAwait(false);
        var asset = releaseFeed?.Assets.FirstOrDefault(candidate =>
            candidate.PackageId == "PlanCope.Local.Host" && candidate.Type == 1 && candidate.FileName == packageFileName);
        if (releaseFeed is null || asset is null) return NotFound();

        // The asset route has no client version in Velopack's URL. Resolve the channel's
        // current gate target with an empty installed version, then permit only that exact
        // package version. This repeats the rollout decision made when the feed was built.
        var decision = await releaseGate.ResolveAsync(nodeId, string.Empty, channel, releaseFeed.LatestVersion, cancellationToken)
            .ConfigureAwait(false);
        if (!decision.MayInstall || !string.Equals(decision.TargetVersion, asset.Version, StringComparison.Ordinal)) return NotFound();

        var download = await installerStorage.GetUpdatePackageDownloadAsync(channel, asset.Version, packageFileName, cancellationToken)
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
}

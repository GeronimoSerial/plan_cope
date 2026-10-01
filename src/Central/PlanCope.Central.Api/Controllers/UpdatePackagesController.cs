using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/updates")]
public sealed class UpdatePackagesController(IInstallerStorage installerStorage) : ControllerBase
{
    [HttpGet("{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        if (!NodeAccessAuth.TryGetNodeId(User, out _)) return Forbid();
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

        var download = await installerStorage.GetAssetDownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
        if (download is null) return NotFound();

        using (download)
        {
            Response.ContentType = download.ContentType;
            Response.Headers.ContentDisposition = $"attachment; filename=\"{download.FileName}\"";
            if (download.ContentLength is { } length) Response.ContentLength = length;
            await download.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        }

        return new EmptyResult();
    }
}

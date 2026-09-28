using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/downloads")]
public sealed class DownloadController(IInstallerStorage installerStorage) : ControllerBase
{
    /// <summary>
    /// Returns the latest published installer reference for a channel. <see cref="InstallerReference.DownloadUrl"/>
    /// is a path on this API (see <see cref="DownloadLatestInstaller"/>) — the client follows it
    /// directly, same-origin, with its existing session; it never sees the private GitHub URL.
    /// </summary>
    [HttpGet("installer/latest")]
    public async Task<IActionResult> GetLatestInstaller(string channel = "stable", CancellationToken cancellationToken = default)
    {
        var installer = await installerStorage.GetLatestAsync(channel, cancellationToken).ConfigureAwait(false);
        if (installer is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = $"No installer published yet for channel '{channel}' (installer storage not configured or nothing released)." });
        }

        return Ok(installer);
    }

    /// <summary>
    /// Streams the installer bytes for a channel using the server-side GitHub token. This is what
    /// makes the Descargas page's download link actually work in a browser: the metadata endpoint
    /// above never hands out a private, GitHub-authenticated URL for the client to follow itself.
    /// </summary>
    [HttpGet("installer/file")]
    public async Task<IActionResult> DownloadLatestInstaller(string channel = "stable", CancellationToken cancellationToken = default)
    {
        if (!installerStorage.IsConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = $"No installer published yet for channel '{channel}' (installer storage not configured or nothing released)." });
        }

        var download = await installerStorage.GetLatestDownloadAsync(channel, cancellationToken).ConfigureAwait(false);
        if (download is null)
        {
            return NotFound(new { error = $"No installer available for channel '{channel}'." });
        }

        using (download)
        {
            Response.ContentType = download.ContentType;
            Response.Headers.ContentDisposition = $"attachment; filename=\"{download.FileName}\"";
            if (download.ContentLength is { } length)
            {
                Response.ContentLength = length;
            }

            await download.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        }

        return new EmptyResult();
    }
}

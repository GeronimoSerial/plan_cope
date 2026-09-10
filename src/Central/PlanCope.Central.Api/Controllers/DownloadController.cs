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
    /// Returns the latest published installer reference for a channel. This endpoint does NOT
    /// proxy/stream the installer bytes — the client is expected to follow
    /// <see cref="InstallerReference.DownloadUrl"/> directly (a private, authenticated URL).
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
}
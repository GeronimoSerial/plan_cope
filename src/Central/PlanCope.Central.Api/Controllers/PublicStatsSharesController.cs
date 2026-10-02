using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Stats;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/stats")]
public sealed class PublicStatsSharesController(StatsShareService shares) : ControllerBase
{
    [HttpGet("{token}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<PublicStatsShareDto>> Read(string token, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var snapshot = await shares.FindPublicAsync(token, ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }
}

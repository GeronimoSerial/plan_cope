using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/stats")]
public sealed class StatsRollupAdminController(CentralStatsRollupService rollupService) : ControllerBase
{
    [HttpPost("rebuild")]
    public async Task<ActionResult<StatsRollupRebuildResponse>> Rebuild(CancellationToken cancellationToken)
    {
        try
        {
            var rebuilt = await rollupService.RebuildAllAsync(cancellationToken);
            return Ok(new StatsRollupRebuildResponse(rebuilt));
        }
        catch (StatsRollupRebuildInProgressException)
        {
            return Conflict(new { error = "A statistics rebuild is already running." });
        }
    }
}

public sealed record StatsRollupRebuildResponse(int RebuiltAttemptCount);

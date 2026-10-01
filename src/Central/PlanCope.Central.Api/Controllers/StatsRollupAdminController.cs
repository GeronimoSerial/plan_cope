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
        var rebuilt = await rollupService.RebuildAllAsync(cancellationToken);
        return Ok(new StatsRollupRebuildResponse(rebuilt));
    }
}

public sealed record StatsRollupRebuildResponse(int RebuiltAttemptCount);

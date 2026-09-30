using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/rosters")]
public sealed class RosterSyncAdminController(RosterSyncCoordinator coordinator) : ControllerBase
{
    [HttpPost("sync-all")]
    public async Task<ActionResult<RosterSyncAllResult>> SyncAll(CancellationToken cancellationToken = default)
    {
        var result = await coordinator.SyncAllAsync(cancellationToken);
        return result.Busy ? Conflict(result) : Ok(result);
    }
}

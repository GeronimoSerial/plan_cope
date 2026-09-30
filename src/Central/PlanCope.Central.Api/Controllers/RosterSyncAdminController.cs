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
    public ActionResult<RosterSyncRunStatus> SyncAll()
    {
        var status = coordinator.StartBackgroundSync();
        if (!status.Enabled) return StatusCode(StatusCodes.Status503ServiceUnavailable, status);
        return AcceptedAtAction(nameof(GetStatus), status);
    }

    [HttpGet("sync-all/status")]
    public ActionResult<RosterSyncRunStatus> GetStatus() => Ok(coordinator.GetStatus());
}

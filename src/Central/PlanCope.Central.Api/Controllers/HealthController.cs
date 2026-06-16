using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var dbConnected = await dbContext.Database.CanConnectAsync(cancellationToken);

        return Ok(new
        {
            status = "ok",
            service = "central-api",
            db = dbConnected ? "connected" : "disconnected"
        });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Admin;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Read-only role reference data for the admin surface. Any authenticated caller may list roles:
/// the codes are needed to build an assign-role request and are not sensitive on their own.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/roles")]
public sealed class RolesAdminController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleSummaryDto>>> ListRoles(
        CancellationToken cancellationToken = default)
    {
        var roles = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Code)
            .ToListAsync(cancellationToken);

        var summary = roles
            .Select(static role => new RoleSummaryDto(role.Id, role.Code, role.Name, role.Description))
            .ToList();

        return Ok(summary);
    }
}

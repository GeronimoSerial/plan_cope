using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin/stats/shares")]
public sealed class StatsSharesAdminController(StatsShareService shares) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<StatsShareCreatedDto>> Create([FromBody] StatsShareCreateRequest? request, CancellationToken ct)
    {
        if (request is null) return BadRequest("Se requiere la configuración del enlace.");
        var scope = User.FindFirstValue("roster_scope");
        IReadOnlyCollection<string>? cues = null;
        if (scope == "school")
        {
            var normalized = User.FindAll("roster_cue").Select(claim => CueCode.TryNormalize(claim.Value, out var cue) ? cue : null)
                .Where(cue => cue is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
            if (normalized.Length == 0) return Forbid();
            cues = normalized;
        }
        else if (scope != "province")
        {
            return Forbid();
        }

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorId)) return Forbid();
        var result = await shares.CreateAsync(request, actorId, cues, "", HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result.Error is not null) return BadRequest(new { error = result.Error });
        return Ok(result.Created);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 32) return NotFound();
        var scope = User.FindFirstValue("roster_scope");
        if (scope is not "school" and not "province") return Forbid();
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorId)) return Forbid();
        var canRevoke = await shares.RevokeAsync(id, actorId, HttpContext.Connection.RemoteIpAddress?.ToString(), scope == "province", ct);
        return canRevoke ? NoContent() : NotFound();
    }
}

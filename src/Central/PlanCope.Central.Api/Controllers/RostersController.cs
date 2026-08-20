using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Integrations.Ge;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rosters")]
public sealed class RostersController(IGeRosterService rosterService) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<ActionResult<GeRosterRefreshResult>> Refresh(
        [FromBody] GeRosterRefreshRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Cue) || string.IsNullOrWhiteSpace(request.SchoolYear))
        {
            ModelState.AddModelError(string.Empty, "cue and schoolYear are required.");
            return ValidationProblem(ModelState);
        }

        var result = await rosterService.RefreshAsync(request.Cue, request.SchoolYear, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{cue}/{schoolYear}/status")]
    public async Task<ActionResult<GeRosterRefreshResult>> Status(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await rosterService.GetLatestAsync(cue, schoolYear, cancellationToken);
        return snapshot is null
            ? NotFound()
            : Ok(new GeRosterRefreshResult(
                snapshot.Id,
                snapshot.Cue,
                snapshot.SchoolYear,
                snapshot.FetchedAt,
                snapshot.Checksum,
                snapshot.SectionCount,
                snapshot.StudentCount,
                snapshot.Status,
                Created: false));
    }

    [HttpGet("{cue}/{schoolYear}/sections")]
    public async Task<ActionResult<IReadOnlyList<GeRosterSectionStatus>>> Sections(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await rosterService.GetLatestAsync(cue, schoolYear, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(await rosterService.GetSectionsAsync(cue, schoolYear, cancellationToken));
    }
}

public sealed record GeRosterRefreshRequest(string? Cue, string? SchoolYear);

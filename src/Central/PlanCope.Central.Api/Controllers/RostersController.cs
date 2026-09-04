using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Domain.ValueObjects;

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
        if (request is null || !CueCode.TryNormalize(request.Cue, out var cue) || string.IsNullOrWhiteSpace(request.SchoolYear))
        {
            ModelState.AddModelError(nameof(request.Cue), $"cue must contain exactly {CueCode.Length} digits.");
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await rosterService.RefreshAsync(cue, request.SchoolYear, cancellationToken);
            return Ok(result);
        }
        catch (GeRosterEmptyException exception)
        {
            return UnprocessableEntity(new { error = exception.Message });
        }
        catch (GeApiException exception)
        {
            return Problem(
                title: "No se pudo obtener el padrón desde GE.",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpGet("{cue}/{schoolYear}/status")]
    public async Task<ActionResult<GeRosterRefreshResult>> Status(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        if (!CueCode.TryNormalize(cue, out var normalizedCue))
        {
            return BadRequest($"cue must contain exactly {CueCode.Length} digits.");
        }

        var snapshot = await rosterService.GetLatestAsync(normalizedCue, schoolYear, cancellationToken);
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
        if (!CueCode.TryNormalize(cue, out var normalizedCue))
        {
            return BadRequest($"cue must contain exactly {CueCode.Length} digits.");
        }

        var snapshot = await rosterService.GetLatestAsync(normalizedCue, schoolYear, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(await rosterService.GetSectionsAsync(normalizedCue, schoolYear, cancellationToken));
    }
}

public sealed record GeRosterRefreshRequest(string? Cue, string? SchoolYear);

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class RosterSyncController(
    PlanCopeDbContext dbContext,
    IGeRosterService rosterService) : ControllerBase
{
    [HttpGet("roster/{cue}/{schoolYear}")]
    public async Task<ActionResult<GeRosterPackageDto>> GetRoster(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        if (!CueCode.TryNormalize(cue, out cue))
        {
            return BadRequest($"cue must contain exactly {CueCode.Length} digits.");
        }
        schoolYear = schoolYear?.Trim() ?? string.Empty;
        if (schoolYear.Length == 0 || schoolYear.Length > GeRosterTransportLimits.MaxSchoolYearLength)
        {
            return BadRequest("cue and schoolYear are required and must be within the supported limits.");
        }

        var snapshot = await rosterService.GetLatestAsync(cue, schoolYear, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        var sections = await dbContext.GeRosterSections
            .AsNoTracking()
            .Where(section => section.SnapshotId == snapshot.Id)
            .Include(section => section.Students)
            .OrderBy(section => section.Id)
            .ToListAsync(cancellationToken);
        var studentCount = sections.Sum(section => section.Students.Count);
        if (sections.Count > GeRosterTransportLimits.MaxSections || studentCount > GeRosterTransportLimits.MaxStudents)
        {
            return Problem("The roster snapshot exceeds the transport limits.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var cueNumber = long.Parse(snapshot.Cue[..7], System.Globalization.CultureInfo.InvariantCulture);
        var annex = int.Parse(snapshot.Cue[7..], System.Globalization.CultureInfo.InvariantCulture);
        var schoolName = await dbContext.Schools
            .AsNoTracking()
            .Where(school => school.Cue == cueNumber && (school.Annex ?? 0) == annex)
            .Select(school => school.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var package = new GeRosterPackageDto(
            snapshot.Id,
            snapshot.Cue,
            snapshot.SchoolYear,
            snapshot.FetchedAt,
            snapshot.Checksum,
            sections.Count,
            studentCount,
            snapshot.Status,
            sections.Select(section => new GeRosterSectionPackageDto(
                section.Id,
                section.GeSectionId,
                section.Course,
                section.Division,
                section.Level,
                section.Shift,
                section.Students
                    .OrderBy(student => student.Id)
                    .Select(student => new GeRosterStudentPackageDto(
                        student.Id,
                        student.SectionId,
                        student.GePersonId,
                        student.Document,
                        student.FirstName,
                        student.LastName))
                    .ToList()))
                .ToList(),
            schoolName);

        // The checksum is part of the persisted snapshot and is also sent in the
        // package. Recompute it before transport so a corrupt central snapshot
        // cannot be accepted by a node.
        if (!string.Equals(package.Checksum, GeRosterPackageChecksum.Calculate(package), StringComparison.OrdinalIgnoreCase))
        {
            return Problem("The roster snapshot checksum is invalid.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(package);
    }
}

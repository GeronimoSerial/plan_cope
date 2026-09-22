using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Admin;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Administration surface over schools. Creation and CUE-unbounded actions require unbounded
/// admin scope — the Admin role or province roster scope; CUE-scoped actions additionally accept
/// a school-scope caller whose own CUE claims cover the target. Cue is immutable after creation,
/// and deactivation flips Status ("Active"/"Inactive") — never DeletedAt, which no reader in
/// this codebase uses.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/schools")]
public sealed class SchoolsAdminController(
    PlanCopeDbContext dbContext,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SchoolSummaryDto>> CreateSchool(
        [FromBody] SchoolCreateRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!HasUnboundedAdminScope())
        {
            return Forbid();
        }

        if (request is null)
        {
            return BadRequest("Body is required.");
        }

        if (!CueCode.TryNormalize(request.Cue, out var normalizedCue))
        {
            return BadRequest($"Cue must contain exactly {CueCode.Length} digits.");
        }

        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.LocalityId))
        {
            return BadRequest("Code, Name and LocalityId are required.");
        }

        if (!long.TryParse(normalizedCue, out var cue))
        {
            return BadRequest($"Cue must contain exactly {CueCode.Length} digits.");
        }

        // The database also has a unique index on Cue, but callers get a clear 409 here instead
        // of a raw DbUpdateException.
        if (await dbContext.Schools.AnyAsync(candidate => candidate.Cue == cue, cancellationToken))
        {
            return Conflict($"A school with Cue {normalizedCue} already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var school = new School(
            Guid.NewGuid().ToString("N"),
            request.Code,
            cue,
            request.Annex,
            request.Name,
            request.LocalityId,
            "Active",
            null,
            now,
            now);

        dbContext.Schools.Add(school);
        AddAudit(
            "School",
            school.Id,
            "admin.school.create",
            new
            {
                schoolId = school.Id,
                cue = normalizedCue,
                code = request.Code,
                name = request.Name,
                localityId = request.LocalityId,
                annex = request.Annex
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ToSummary(school, normalizedCue));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SchoolSummaryDto>>> ListSchools(
        CancellationToken cancellationToken = default)
    {
        // School scope filters by the caller's own roster_cue claims — never by looking up
        // another user's assignments.
        HashSet<string>? ownCues;
        if (HasUnboundedAdminScope())
        {
            ownCues = null;
        }
        else if (string.Equals(User.FindFirstValue("roster_scope"), "school", StringComparison.Ordinal))
        {
            ownCues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var claim in User.FindAll("roster_cue"))
            {
                ownCues.Add(CueCode.TryNormalize(claim.Value, out var claimCue) ? claimCue : claim.Value);
            }
        }
        else
        {
            return Forbid();
        }

        var schools = await dbContext.Schools
            .AsNoTracking()
            .OrderBy(candidate => candidate.Cue)
            .ToListAsync(cancellationToken);

        var summary = new List<SchoolSummaryDto>(schools.Count);
        foreach (var school in schools)
        {
            var normalizedCue = NormalizeCue(school);
            if (ownCues is not null && !ownCues.Contains(normalizedCue))
            {
                continue;
            }

            summary.Add(ToSummary(school, normalizedCue));
        }

        return Ok(summary);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SchoolSummaryDto>> GetSchool(
        string id,
        CancellationToken cancellationToken = default)
    {
        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (school is null)
        {
            return NotFound();
        }

        if (!HasUnboundedAdminScope() &&
            !(await authorizationService.AuthorizeAsync(User, NormalizeCue(school), new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        return Ok(ToSummary(school, NormalizeCue(school)));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult> UpdateSchool(
        string id,
        [FromBody] SchoolUpdateRequest? request,
        CancellationToken cancellationToken = default)
    {
        var school = await dbContext.Schools
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (school is null)
        {
            return NotFound();
        }

        if (!HasUnboundedAdminScope() &&
            !(await authorizationService.AuthorizeAsync(User, NormalizeCue(school), new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.LocalityId))
        {
            return BadRequest("Name and LocalityId are required.");
        }

        // Cue and Code are immutable: the update covers Name, Annex and LocalityId only.
        dbContext.Entry(school).CurrentValues.SetValues(school with
        {
            Name = request.Name,
            Annex = request.Annex,
            LocalityId = request.LocalityId,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        AddAudit(
            "School",
            school.Id,
            "admin.school.update",
            new
            {
                schoolId = school.Id,
                cue = NormalizeCue(school),
                name = request.Name,
                annex = request.Annex,
                localityId = request.LocalityId
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult> DeactivateSchool(
        string id,
        CancellationToken cancellationToken = default)
    {
        var school = await dbContext.Schools
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (school is null)
        {
            return NotFound();
        }

        if (!HasUnboundedAdminScope() &&
            !(await authorizationService.AuthorizeAsync(User, NormalizeCue(school), new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        // Idempotent: deactivating an already-inactive school is a no-op success. Deactivation is
        // the Status flip only — DeletedAt stays untouched.
        if (!IsActive(school.Status))
        {
            return NoContent();
        }

        dbContext.Entry(school).CurrentValues.SetValues(school with
        {
            Status = "Inactive",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        AddAudit(
            "School",
            school.Id,
            "admin.school.deactivate",
            new
            {
                schoolId = school.Id,
                cue = NormalizeCue(school)
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Whether the caller may administer any user or school regardless of CUE. Province-scope
    /// roster callers already have this by virtue of their roster scope; Admins get it
    /// unconditionally because identity administration is not roster-data access. This must never
    /// be used to grant roster-data (roster/CUE) access — only identity-administration decisions in
    /// this controller.
    /// </summary>
    private bool HasUnboundedAdminScope()
    {
        return User.IsInRole("Admin") ||
            string.Equals(User.FindFirstValue("roster_scope"), "province", StringComparison.Ordinal);
    }

    private static bool IsActive(string status)
    {
        return string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCue(School school)
    {
        // School.Cue is a long; everywhere else (claims, UserSchoolAssignment) a CUE is the
        // normalized digit string.
        var cueText = school.Cue.ToString();
        return CueCode.TryNormalize(cueText, out var normalized) ? normalized : cueText;
    }

    private static SchoolSummaryDto ToSummary(School school, string normalizedCue)
    {
        return new SchoolSummaryDto(
            school.Id,
            normalizedCue,
            school.Code,
            school.Name,
            school.LocalityId,
            school.Annex,
            school.Status);
    }

    private void AddAudit(string entityType, string entityId, string action, object payload)
    {
        dbContext.AuditLogs.Add(new AuditLog(
            Guid.NewGuid().ToString("N"),
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            entityType,
            entityId,
            action,
            JsonDocument.Parse(JsonSerializer.Serialize(payload)),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            DateTimeOffset.UtcNow));
    }
}

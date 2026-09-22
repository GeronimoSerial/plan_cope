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
/// Administration surface over users, role assignments and CUE assignments. Creation and
/// CUE-unbounded actions (creating a user, granting unbounded roles) require unbounded admin
/// scope — the Admin role or province roster scope; CUE-scoped actions additionally accept a
/// school-scope caller whose own CUE claims cover the target. Assigning a CUE checks scope
/// against the CUE being granted — the escalation boundary that lets a school-scope caller
/// grant only CUEs it holds itself.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/users")]
public sealed class UsersAdminController(
    PlanCopeDbContext dbContext,
    IAuthorizationService authorizationService) : ControllerBase
{
    private const int MinPasswordLength = 8;

    // Roles that themselves confer unbounded authority. A caller without unbounded authority
    // (see HasUnboundedAdminScope below) must never be allowed to grant one of these — otherwise
    // a school-scope caller could grant themselves province-wide access.
    private static readonly HashSet<string> UnboundedScopeRoles = new(StringComparer.Ordinal)
    {
        "Admin",
        "RosterProvince",
    };

    [HttpPost]
    public async Task<ActionResult<UserSummaryDto>> CreateUser(
        [FromBody] UserCreateRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!HasUnboundedAdminScope())
        {
            return Forbid();
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest("Email, Password and FullName are required.");
        }

        if (request.Password.Length < MinPasswordLength)
        {
            return BadRequest($"Password must be at least {MinPasswordLength} characters.");
        }

        var email = request.Email.Trim();

        // The database also has a unique index on Email, but callers get a clear 409 here instead
        // of a raw DbUpdateException — including when a soft-deleted row still holds the address.
        if (await dbContext.Users.AnyAsync(candidate => candidate.Email == email, cancellationToken))
        {
            return Conflict($"A user with email {email} already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User(
            Guid.NewGuid().ToString("N"),
            email,
            BCrypt.Net.BCrypt.HashPassword(request.Password),
            request.FullName,
            "Active",
            null,
            null,
            now,
            now);

        dbContext.Users.Add(user);
        AddAudit(
            "User",
            user.Id,
            "admin.user.create",
            new
            {
                userId = user.Id,
                email = user.Email,
                fullName = user.FullName
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new UserSummaryDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Status,
            [],
            []));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> ListUsers(
        CancellationToken cancellationToken = default)
    {
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

        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(candidate => candidate.Email)
            .ToListAsync(cancellationToken);

        var assignments = await dbContext.UserSchools
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var userRoleRows = await (
            from assignment in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on assignment.RoleId equals role.Id
            select new { assignment.UserId, RoleCode = role.Code })
            .ToListAsync(cancellationToken);

        var cuesByUser = new Dictionary<string, List<string>>();
        foreach (var assignment in assignments)
        {
            if (!CueCode.TryNormalize(assignment.Cue, out var normalized))
            {
                continue;
            }

            if (!cuesByUser.TryGetValue(assignment.UserId, out var cues))
            {
                cues = new List<string>();
                cuesByUser[assignment.UserId] = cues;
            }

            if (!cues.Contains(normalized))
            {
                cues.Add(normalized);
            }
        }

        var roleCodesByUser = new Dictionary<string, List<string>>();
        foreach (var row in userRoleRows)
        {
            if (!roleCodesByUser.TryGetValue(row.UserId, out var roleCodes))
            {
                roleCodes = new List<string>();
                roleCodesByUser[row.UserId] = roleCodes;
            }

            if (!roleCodes.Contains(row.RoleCode))
            {
                roleCodes.Add(row.RoleCode);
            }
        }

        foreach (var cues in cuesByUser.Values)
        {
            cues.Sort(StringComparer.Ordinal);
        }

        foreach (var roleCodes in roleCodesByUser.Values)
        {
            roleCodes.Sort(StringComparer.Ordinal);
        }

        var summary = new List<UserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            IReadOnlyList<string> cues = cuesByUser.TryGetValue(user.Id, out var userCues) ? userCues : [];
            if (ownCues is not null && !cues.Any(ownCues.Contains))
            {
                continue;
            }

            IReadOnlyList<string> roleCodes = roleCodesByUser.TryGetValue(user.Id, out var userRoleCodes) ? userRoleCodes : [];
            summary.Add(new UserSummaryDto(user.Id, user.Email, user.FullName, user.Status, cues, roleCodes));
        }

        return Ok(summary);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserSummaryDto>> GetUser(
        string id,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!(await CanManageUserAsync(user, cancellationToken)))
        {
            return Forbid();
        }

        return Ok(await BuildSummaryAsync(user, cancellationToken));
    }

    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult> DeactivateUser(
        string id,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!(await CanManageUserAsync(user, cancellationToken)))
        {
            return Forbid();
        }

        // Idempotent: deactivating an already-inactive user is a no-op success. Deactivation is
        // the Status flip only — DeletedAt stays untouched.
        if (!IsActive(user.Status))
        {
            return NoContent();
        }

        dbContext.Entry(user).CurrentValues.SetValues(user with
        {
            Status = "Inactive",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        AddAudit(
            "User",
            user.Id,
            "admin.user.deactivate",
            new
            {
                userId = user.Id,
                email = user.Email
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult> ResetPassword(
        string id,
        [FromBody] ResetPasswordRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!(await CanManageUserAsync(user, cancellationToken)))
        {
            return Forbid();
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < MinPasswordLength)
        {
            return BadRequest($"NewPassword must be at least {MinPasswordLength} characters.");
        }

        dbContext.Entry(user).CurrentValues.SetValues(user with
        {
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword),
            UpdatedAt = DateTimeOffset.UtcNow
        });

        // The payload records who ordered the reset only — never the password or its hash.
        AddAudit(
            "User",
            user.Id,
            "admin.user.reset_password",
            new
            {
                userId = user.Id,
                resetBy = User.FindFirstValue(ClaimTypes.NameIdentifier)
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/roles")]
    public async Task<ActionResult> AssignRole(
        string id,
        [FromBody] AssignRoleRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!(await CanManageUserAsync(user, cancellationToken)))
        {
            return Forbid();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.RoleCode))
        {
            return BadRequest("RoleCode is required.");
        }

        var roleCode = request.RoleCode.Trim();
        var role = await dbContext.Roles
            .SingleOrDefaultAsync(candidate => candidate.Code == roleCode, cancellationToken);
        if (role is null)
        {
            return NotFound($"Role {roleCode} does not exist.");
        }

        if (UnboundedScopeRoles.Contains(role.Code) && !HasUnboundedAdminScope())
        {
            return Forbid();
        }

        // Idempotent: assigning an already-assigned role is a no-op success.
        var alreadyAssigned = await dbContext.UserRoles
            .AnyAsync(
                assignment => assignment.UserId == user.Id && assignment.RoleId == role.Id,
                cancellationToken);
        if (alreadyAssigned)
        {
            return NoContent();
        }

        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, role.Id, DateTimeOffset.UtcNow));
        AddAudit(
            "UserRoleAssignment",
            $"{user.Id}:{role.Code}",
            "admin.user.role.assign",
            new
            {
                userId = user.Id,
                roleCode = role.Code
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}/roles/{roleCode}")]
    public async Task<ActionResult> RevokeRole(
        string id,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!(await CanManageUserAsync(user, cancellationToken)))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return BadRequest("roleCode is required.");
        }

        var trimmedRoleCode = roleCode.Trim();
        var role = await dbContext.Roles
            .SingleOrDefaultAsync(candidate => candidate.Code == trimmedRoleCode, cancellationToken);
        if (role is null)
        {
            // Idempotent: a role that does not exist cannot be assigned, so there is nothing to
            // revoke — not-currently-assigned is a no-op success.
            return NoContent();
        }

        var assignment = await dbContext.UserRoles
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == user.Id && candidate.RoleId == role.Id,
                cancellationToken);
        if (assignment is null)
        {
            // Idempotent: not-currently-assigned is a no-op success.
            return NoContent();
        }

        dbContext.UserRoles.Remove(assignment);
        AddAudit(
            "UserRoleAssignment",
            $"{user.Id}:{role.Code}",
            "admin.user.role.revoke",
            new
            {
                userId = user.Id,
                roleCode = role.Code
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/schools")]
    public async Task<ActionResult> AssignSchool(
        string id,
        [FromBody] AssignSchoolRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (request is null || !CueCode.TryNormalize(request.Cue, out var cue))
        {
            return BadRequest($"Cue must contain exactly {CueCode.Length} digits.");
        }

        // Escalation boundary: scope is checked against the CUE being granted, not against the
        // user's existing CUEs — a school-scope caller can only ever grant a CUE it holds itself.
        if (!(await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        var assignedCues = await dbContext.UserSchools
            .AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .Select(assignment => assignment.Cue)
            .ToListAsync(cancellationToken);

        foreach (var assignedCue in assignedCues)
        {
            if (CueCode.TryNormalize(assignedCue, out var normalized) &&
                string.Equals(normalized, cue, StringComparison.Ordinal))
            {
                // Idempotent: assigning an already-assigned CUE is a no-op success.
                return NoContent();
            }
        }

        dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, cue, DateTimeOffset.UtcNow));
        AddAudit(
            "UserSchoolAssignment",
            $"{user.Id}:{cue}",
            "admin.user.school.assign",
            new
            {
                userId = user.Id,
                cue
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}/schools/{cue}")]
    public async Task<ActionResult> RevokeSchool(
        string id,
        string cue,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!CueCode.TryNormalize(cue, out var normalizedCue))
        {
            return BadRequest($"Cue must contain exactly {CueCode.Length} digits.");
        }

        // Revocation checks scope against the CUE in the route — the same rule as assign: the
        // caller can only ever act on CUEs it holds itself.
        if (!(await authorizationService.AuthorizeAsync(User, normalizedCue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        var assignments = await dbContext.UserSchools
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync(cancellationToken);

        UserSchoolAssignment? matched = null;
        foreach (var assignment in assignments)
        {
            if (CueCode.TryNormalize(assignment.Cue, out var assignedCue) &&
                string.Equals(assignedCue, normalizedCue, StringComparison.Ordinal))
            {
                matched = assignment;
                break;
            }
        }

        if (matched is null)
        {
            // Idempotent: revoking a CUE that is not currently assigned is a no-op success.
            return NoContent();
        }

        dbContext.UserSchools.Remove(matched);
        AddAudit(
            "UserSchoolAssignment",
            $"{user.Id}:{normalizedCue}",
            "admin.user.school.revoke",
            new
            {
                userId = user.Id,
                cue = normalizedCue
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

    /// <summary>
    /// Decides whether the caller may act on this user. Unbounded admin scope always may; school
    /// scope only if at least one of the target user's CUE assignments passes the roster-scope
    /// check. A user with zero assignments is therefore unbounded-scope-readable only.
    /// </summary>
    private async Task<bool> CanManageUserAsync(User user, CancellationToken cancellationToken)
    {
        if (HasUnboundedAdminScope())
        {
            return true;
        }

        var assignmentCues = await dbContext.UserSchools
            .AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .Select(assignment => assignment.Cue)
            .ToListAsync(cancellationToken);

        foreach (var cue in assignmentCues)
        {
            if ((await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<UserSummaryDto> BuildSummaryAsync(User user, CancellationToken cancellationToken)
    {
        var assignedCues = await dbContext.UserSchools
            .AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .Select(assignment => assignment.Cue)
            .ToListAsync(cancellationToken);

        var cues = new List<string>(assignedCues.Count);
        foreach (var cue in assignedCues)
        {
            if (CueCode.TryNormalize(cue, out var normalized) && !cues.Contains(normalized))
            {
                cues.Add(normalized);
            }
        }

        cues.Sort(StringComparer.Ordinal);

        var roleCodes = await (
            from assignment in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on assignment.RoleId equals role.Id
            where assignment.UserId == user.Id
            orderby role.Code
            select role.Code)
            .ToListAsync(cancellationToken);

        return new UserSummaryDto(user.Id, user.Email, user.FullName, user.Status, cues, roleCodes);
    }

    private static bool IsActive(string status)
    {
        return string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
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

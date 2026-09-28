using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Admin;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Administration surface over release rings. Rings are universal infrastructure — Channel is the
/// partition key (e.g. "stable"/"beta"), not a school — so there is no CUE filtering here: every
/// operation, including the list, requires unbounded admin scope (the Admin role or province
/// roster scope) and gets Forbid() otherwise. Version, Channel, Sha256 and DownloadUrl are set
/// once at creation; only the rollout policy (RolloutMode/RolloutPercentage) is updatable, and
/// rings are never deleted because ReleaseGateService resolves the newest row per channel.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/release-rings")]
public sealed class ReleaseRingsAdminController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReleaseRingSummaryDto>>> ListReleaseRings(
        CancellationToken cancellationToken = default)
    {
        if (!HasUnboundedAdminScope())
        {
            return Forbid();
        }

        var rings = await dbContext.ReleaseRings
            .AsNoTracking()
            .OrderBy(candidate => candidate.Channel)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(rings.Select(ToSummary).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ReleaseRingSummaryDto>> CreateReleaseRing(
        [FromBody] ReleaseRingCreateRequest? request,
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

        if (string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.Channel) ||
            string.IsNullOrWhiteSpace(request.Sha256) ||
            string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            return BadRequest("Version, Channel, Sha256 and DownloadUrl are required.");
        }

        var rolloutError = ValidateRollout(request.RolloutMode, request.RolloutPercentage, out var rolloutPercentage);
        if (rolloutError is not null)
        {
            return BadRequest(rolloutError);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var createdBy))
        {
            return Unauthorized();
        }

        var ring = new ReleaseRing(
            Guid.NewGuid(),
            request.Version,
            request.Channel,
            request.Sha256,
            request.DownloadUrl,
            request.RolloutMode,
            rolloutPercentage,
            DateTimeOffset.UtcNow,
            createdBy);

        dbContext.ReleaseRings.Add(ring);
        AddAudit(
            "ReleaseRing",
            ring.Id.ToString(),
            "admin.release_ring.create",
            new
            {
                ringId = ring.Id,
                version = ring.Version,
                channel = ring.Channel,
                sha256 = ring.Sha256,
                downloadUrl = ring.DownloadUrl,
                rolloutMode = ring.RolloutMode,
                rolloutPercentage = ring.RolloutPercentage
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ToSummary(ring));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult> UpdateReleaseRing(
        Guid id,
        [FromBody] ReleaseRingUpdateRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!HasUnboundedAdminScope())
        {
            return Forbid();
        }

        var ring = await dbContext.ReleaseRings
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (ring is null)
        {
            return NotFound();
        }

        if (request is null)
        {
            return BadRequest("Body is required.");
        }

        var rolloutError = ValidateRollout(request.RolloutMode, request.RolloutPercentage, out var rolloutPercentage);
        if (rolloutError is not null)
        {
            return BadRequest(rolloutError);
        }

        // Version, Channel, Sha256, DownloadUrl, CreatedAt and CreatedBy are immutable: the
        // update covers the rollout policy only.
        dbContext.Entry(ring).CurrentValues.SetValues(ring with
        {
            RolloutMode = request.RolloutMode,
            RolloutPercentage = rolloutPercentage
        });

        AddAudit(
            "ReleaseRing",
            ring.Id.ToString(),
            "admin.release_ring.update",
            new
            {
                ringId = ring.Id,
                channel = ring.Channel,
                rolloutMode = request.RolloutMode,
                rolloutPercentage = rolloutPercentage
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Whether the caller may administer release rings regardless of roster scope. Release rings
    /// are universal infrastructure (a ReleaseRing carries no CUE), so unlike the roster-data
    /// surfaces there is no CUE-scoped fallback path — Admins and province-scope roster callers
    /// only.
    /// </summary>
    private bool HasUnboundedAdminScope()
    {
        return User.IsInRole("Admin") ||
            string.Equals(User.FindFirstValue("roster_scope"), "province", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates the rollout policy shared by create and update. Returns null when the policy is
    /// valid; otherwise a message for a 400. For "PercentageOfEnrolled" the percentage must be
    /// present and within 0..100; every other mode stores a null percentage regardless of what
    /// the caller sent, mirroring ReleaseGateService only consulting the percentage for that
    /// single mode.
    /// </summary>
    private static string? ValidateRollout(string rolloutMode, int? rolloutPercentage, out int? normalizedPercentage)
    {
        if (!string.Equals(rolloutMode, "AllEnrolled", StringComparison.Ordinal) &&
            !string.Equals(rolloutMode, "PercentageOfEnrolled", StringComparison.Ordinal) &&
            !string.Equals(rolloutMode, "ExplicitList", StringComparison.Ordinal))
        {
            normalizedPercentage = null;
            return "RolloutMode must be one of AllEnrolled, PercentageOfEnrolled or ExplicitList.";
        }

        if (string.Equals(rolloutMode, "PercentageOfEnrolled", StringComparison.Ordinal))
        {
            if (rolloutPercentage is null)
            {
                normalizedPercentage = null;
                return "RolloutPercentage is required for the PercentageOfEnrolled rollout mode.";
            }

            if (rolloutPercentage is < 0 or > 100)
            {
                normalizedPercentage = null;
                return "RolloutPercentage must be between 0 and 100.";
            }

            normalizedPercentage = rolloutPercentage;
            return null;
        }

        normalizedPercentage = null;
        return null;
    }

    private static ReleaseRingSummaryDto ToSummary(ReleaseRing ring)
    {
        return new ReleaseRingSummaryDto(
            ring.Id,
            ring.Version,
            ring.Channel,
            ring.Sha256,
            ring.DownloadUrl,
            ring.RolloutMode,
            ring.RolloutPercentage,
            ring.CreatedAt,
            ring.CreatedBy);
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

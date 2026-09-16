using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Admin operations over activation keys and registered nodes. Keys are universal (ScopeCue is
/// always null): roster scoping here governs who may administer a key record, never where the
/// key may be redeemed. Key revocation and node revocation are deliberately two separate
/// endpoints with two separate blast radii.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/activation")]
public sealed class ActivationAdminController(
    PlanCopeDbContext dbContext,
    ActivationKeyService keyService,
    IAuthorizationService authorizationService) : ControllerBase
{
    private const string IssuedForCueToken = "issued-for-cue:";
    private const int MaxNoteLength = 512;
    private const int MaxRevokedReasonLength = 256;

    [HttpPost("keys")]
    public async Task<ActionResult<IssueActivationKeyResponse>> IssueKey(
        [FromBody] IssueActivationKeyRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.MaxActivations < 1)
        {
            return BadRequest("MaxActivations must be at least 1.");
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return BadRequest("ExpiresAt must be in the future.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var issuedForCue = await ResolveIssuedForCueAsync(request.Note, cancellationToken);
        if (!issuedForCue.Allowed)
        {
            return issuedForCue.Error ?? BadRequest();
        }

        var generated = keyService.Generate();
        var key = new ActivationKey(
            Guid.NewGuid().ToString("N"),
            generated.KeyHash,
            generated.KeyPrefix,
            userId,
            DateTimeOffset.UtcNow,
            request.ExpiresAt,
            request.MaxActivations,
            ActivationCount: 0,
            RevokedAt: null,
            RevokedReason: null,
            ScopeCue: null,
            Note: BuildNote(request.Note, issuedForCue.Cue));

        dbContext.ActivationKeys.Add(key);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new IssueActivationKeyResponse(
                key.Id,
                generated.PlaintextKey,
                key.KeyPrefix,
                key.IssuedAt,
                key.ExpiresAt,
                key.MaxActivations));
    }

    [HttpGet("keys")]
    public async Task<ActionResult<IReadOnlyList<ActivationKeySummaryDto>>> ListKeys(
        CancellationToken cancellationToken = default)
    {
        var administerableIssuers = await GetAdministerableIssuerIdsAsync(cancellationToken);

        var keysQuery = dbContext.ActivationKeys.AsNoTracking();
        if (administerableIssuers is not null)
        {
            keysQuery = keysQuery.Where(key => administerableIssuers.Contains(key.IssuedBy));
        }

        var keys = await keysQuery
            .OrderByDescending(key => key.IssuedAt)
            .ToListAsync(cancellationToken);

        var summary = keys.Select(static key => new ActivationKeySummaryDto(
            key.Id,
            key.KeyPrefix,
            key.IssuedAt,
            key.ExpiresAt,
            key.MaxActivations,
            key.ActivationCount,
            key.RevokedAt,
            key.RevokedReason,
            key.Note)).ToList();

        return Ok(summary);
    }

    [HttpPost("keys/{id}/revoke")]
    public async Task<ActionResult> RevokeKey(
        string id,
        [FromBody] RevokeActivationKeyRequest? request,
        CancellationToken cancellationToken = default)
    {
        var key = await dbContext.ActivationKeys
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (key is null)
        {
            return NotFound();
        }

        if (!(await CanAdministerKeyAsync(key, cancellationToken)))
        {
            return Forbid();
        }

        // Idempotent: revoking an already-revoked key is a no-op success. Revoking a key stops
        // future enrolments through it; nodes already enrolled keep working untouched.
        if (key.RevokedAt is null)
        {
            dbContext.Entry(key).CurrentValues.SetValues(key with
            {
                RevokedAt = DateTimeOffset.UtcNow,
                RevokedReason = Truncate(request?.Reason, MaxRevokedReasonLength)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpGet("nodes")]
    public async Task<ActionResult<IReadOnlyList<RegisteredNodeSummaryDto>>> ListNodes(
        CancellationToken cancellationToken = default)
    {
        var nodes = await dbContext.RegisteredNodes
            .AsNoTracking()
            .OrderByDescending(node => node.EnrolledAt)
            .ToListAsync(cancellationToken);

        var summary = new List<RegisteredNodeSummaryDto>(nodes.Count);
        foreach (var node in nodes)
        {
            if ((await authorizationService.AuthorizeAsync(User, node.Cue, new RosterScopeRequirement())).Succeeded)
            {
                summary.Add(new RegisteredNodeSummaryDto(
                    node.Id,
                    node.NodeCode,
                    node.Cue,
                    node.DeviceName,
                    node.EnrolledAt,
                    node.LastSeenAt,
                    node.RevokedAt));
            }
        }

        return Ok(summary);
    }

    [HttpPost("nodes/{id}/revoke")]
    public async Task<ActionResult> RevokeNode(
        string id,
        [FromBody] RevokeNodeRequest? request,
        CancellationToken cancellationToken = default)
    {
        var node = await dbContext.RegisteredNodes
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (node is null)
        {
            return NotFound();
        }

        if (!(await authorizationService.AuthorizeAsync(User, node.Cue, new RosterScopeRequirement())).Succeeded)
        {
            return Forbid();
        }

        // Idempotent: revoking an already-revoked node is a no-op success. Node revocation stops
        // only this machine and must never touch the ActivationKey that enrolled it — the two
        // levels are independent. RegisteredNode has no revocation-reason column, so the reason
        // body is accepted for symmetry but persisted by the audit slice, not here.
        if (node.RevokedAt is null)
        {
            dbContext.Entry(node).CurrentValues.SetValues(node with { RevokedAt = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>
    /// Decides which CUE a school-scope caller issues a key "for" (pure bookkeeping). Province
    /// scope has no forced CUE. A school-scope caller with exactly one assigned CUE resolves to
    /// that CUE; with several, the request must name one as plain text in the note
    /// ("issued-for-cue:180000100"). The resolved CUE is stored in <see cref="ActivationKey.Note"/>
    /// only — <see cref="ActivationKey.ScopeCue"/> stays null because keys are universal.
    /// </summary>
    private async Task<CueResolution> ResolveIssuedForCueAsync(string? note, CancellationToken cancellationToken)
    {
        if (string.Equals(User.FindFirstValue("roster_scope"), "province", StringComparison.Ordinal))
        {
            return new CueResolution(true, null, null);
        }

        var rosterCues = User.FindAll("roster_cue").Select(static claim => claim.Value).ToList();
        if (rosterCues.Count == 0)
        {
            return new CueResolution(false, null, BadRequest("A school-scope caller needs at least one assigned CUE to issue keys."));
        }

        if (rosterCues.Count == 1)
        {
            return new CueResolution(true, rosterCues[0], null);
        }

        var namedCue = TryExtractIssuedForCue(note);
        if (namedCue is null || !(await authorizationService.AuthorizeAsync(User, namedCue, new RosterScopeRequirement())).Succeeded)
        {
            return new CueResolution(false, null, BadRequest($"Note must name one of your assigned CUEs as {IssuedForCueToken}<cue>."));
        }

        return new CueResolution(true, namedCue, null);
    }

    /// <summary>
    /// Resolves the ActivationKey.IssuedBy values the caller may administer. Province scope
    /// administers every issuer (null = unbounded). School scope administers issuers assigned to
    /// a CUE the caller can access — decided per CUE by the RosterScopeAuthorizationHandler and
    /// mapped to issuers through the UserSchools join, because keys themselves carry no CUE.
    /// </summary>
    private async Task<HashSet<string>?> GetAdministerableIssuerIdsAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(User.FindFirstValue("roster_scope"), "province", StringComparison.Ordinal))
        {
            return null;
        }

        var candidateCues = await dbContext.UserSchools
            .AsNoTracking()
            .Select(static assignment => assignment.Cue)
            .Distinct()
            .ToListAsync(cancellationToken);

        var authorizedCues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cue in candidateCues)
        {
            if ((await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
            {
                authorizedCues.Add(cue);
            }
        }

        var issuerIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(assignment => authorizedCues.Contains(assignment.Cue))
            .Select(static assignment => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return issuerIds.ToHashSet();
    }

    private async Task<bool> CanAdministerKeyAsync(ActivationKey key, CancellationToken cancellationToken)
    {
        if (string.Equals(User.FindFirstValue("roster_scope"), "province", StringComparison.Ordinal))
        {
            return true;
        }

        var issuerCues = await dbContext.UserSchools
            .AsNoTracking()
            .Where(assignment => assignment.UserId == key.IssuedBy)
            .Select(static assignment => assignment.Cue)
            .ToListAsync(cancellationToken);

        foreach (var cue in issuerCues)
        {
            if ((await authorizationService.AuthorizeAsync(User, cue, new RosterScopeRequirement())).Succeeded)
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExtractIssuedForCue(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var index = note.IndexOf(IssuedForCueToken, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + IssuedForCueToken.Length;
        var end = start;
        while (end < note.Length && !char.IsWhiteSpace(note[end]))
        {
            end++;
        }

        return CueCode.TryNormalize(note[start..end], out var cue) ? cue : null;
    }

    private static string? BuildNote(string? note, string? issuedForCue)
    {
        var trimmed = note?.Trim() ?? string.Empty;
        var token = issuedForCue is null ? null : $"{IssuedForCueToken}{issuedForCue}";
        if (token is null)
        {
            return string.IsNullOrEmpty(trimmed) ? null : Truncate(trimmed, MaxNoteLength);
        }

        // Strip any token the caller already wrote so the canonical one appears exactly once.
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static part => !part.StartsWith(IssuedForCueToken, StringComparison.Ordinal))
            .ToArray();
        var stripped = string.Join(' ', parts);

        var result = string.IsNullOrEmpty(stripped) ? token : $"{stripped} {token}";
        return Truncate(result, MaxNoteLength);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record CueResolution(bool Allowed, string? Cue, ActionResult<IssueActivationKeyResponse>? Error);
}
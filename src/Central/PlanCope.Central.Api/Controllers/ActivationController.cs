using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Node-side activation endpoints. Both are anonymous: the activation key (redeem) and the
/// refresh token (refresh) are the credentials. No logger is held anywhere in this path because
/// the only useful context would be key material or refresh-token secrets, which must never be
/// logged.
/// </summary>
[ApiController]
[Route("api/activation")]
public sealed class ActivationController(
    PlanCopeDbContext dbContext,
    ActivationKeyService keyService,
    NodeCredentialService credentialService) : ControllerBase
{
    [HttpPost("redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivationRedeemResult>> Redeem(
        [FromBody] ActivationRedeemRequest request,
        CancellationToken cancellationToken)
    {
        // Malformed keys are rejected before any database access — see B1 acceptance criteria.
        if (!ActivationKeyService.TryNormalize(request.ActivationKey, out var canonical))
        {
            return BadRequest(ActivationRedeemResult.Failed(ActivationRedeemFailureReason.MalformedKey));
        }

        var keyPrefix = canonical[..8];
        var candidates = await dbContext.ActivationKeys
            .Where(key => key.KeyPrefix == keyPrefix)
            .ToListAsync(cancellationToken);
        var key = candidates.FirstOrDefault(candidate => keyService.HashMatches(canonical, candidate));
        if (key is null)
        {
            return NotFound(ActivationRedeemResult.Failed(ActivationRedeemFailureReason.KeyNotFound));
        }

        // Revocation is the strongest signal (an admin explicitly killed the key), then expiry (dead
        // regardless of remaining count), then exhaustion (weakest — only when the key is otherwise
        // healthy). First match wins so the client always learns the most decisive reason.
        if (ActivationKeyService.IsRevoked(key))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ActivationRedeemResult.Failed(ActivationRedeemFailureReason.KeyRevoked));
        }

        if (ActivationKeyService.IsExpired(key, DateTimeOffset.UtcNow))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ActivationRedeemResult.Failed(ActivationRedeemFailureReason.KeyExpired));
        }

        if (ActivationKeyService.IsExhausted(key))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ActivationRedeemResult.Failed(ActivationRedeemFailureReason.ActivationLimitReached));
        }

        var (node, isNewNode) = await credentialService.FindOrEnrollAsync(
            key,
            request.Cue,
            request.FingerprintHash,
            request.FingerprintComponents,
            request.AppVersion,
            cancellationToken);
        var credentials = await credentialService.IssueForNodeAsync(node, cancellationToken);

        dbContext.AuditLogs.Add(new AuditLog(
            Guid.NewGuid().ToString("N"),
            ActorId: null,
            "RegisteredNode",
            node.Id,
            "activation.redeem",
            JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                keyPrefix,
                cue = node.Cue,
                fingerprintHash = node.FingerprintHash,
                isNewNode
            })),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ActivationRedeemResult.Succeeded(new ActivationRedeemResponse
        {
            NodeId = node.Id,
            AccessToken = credentials.AccessToken,
            RefreshToken = credentials.PlaintextRefreshToken,
            AccessTokenExpiresAt = credentials.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = credentials.RefreshTokenExpiresAt
        }));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivationRefreshResponse>> Refresh(
        [FromBody] ActivationRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var rotation = await credentialService.RotateAsync(request.RefreshToken, cancellationToken);
        if (rotation is null)
        {
            return Unauthorized();
        }

        // Rotation still succeeds when the node is revoked so it keeps operating until its current
        // access token expires; NodeRevoked is the one signal on this channel that B5 will later
        // use to detect revocation (per plan task 8).
        return Ok(new ActivationRefreshResponse
        {
            AccessToken = rotation.Credentials.AccessToken,
            RefreshToken = rotation.Credentials.PlaintextRefreshToken,
            AccessTokenExpiresAt = rotation.Credentials.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = rotation.Credentials.RefreshTokenExpiresAt,
            NodeRevoked = rotation.Node.RevokedAt is not null
        });
    }
}
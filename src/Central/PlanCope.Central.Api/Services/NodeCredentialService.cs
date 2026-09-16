using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Issues, rotates and revokes node credentials. Access tokens are minted by
/// <see cref="ITokenService"/>; refresh tokens are opaque 256-bit random secrets of which only
/// the SHA-256 hash is ever persisted — the plaintext is returned to the caller exactly once.
/// </summary>
public sealed class NodeCredentialService
{
    // Access tokens are short-lived so a leaked token is a small blast radius; the paired refresh
    // token rotates a replacement long before it matters.
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(45);

    // Refresh tokens live 60 days and are retired on first use (rotation), so a single stolen
    // token stops working at the next refresh.
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(60);

    private readonly PlanCopeDbContext _dbContext;
    private readonly ITokenService _tokenService;

    public NodeCredentialService(PlanCopeDbContext dbContext, ITokenService tokenService)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Mints an access token plus a fresh refresh token for <paramref name="node"/>. The
    /// credential row is only added to the context here — the caller commits the transaction so a
    /// rotation can revoke the spent credential and persist its replacement in one
    /// SaveChangesAsync. Returns the plaintext refresh token; the persisted entity holds only its
    /// hash.
    /// </summary>
    public Task<IssuedCredentials> IssueForNodeAsync(
        RegisteredNode node,
        CancellationToken cancellationToken,
        string? rotatedFrom = null)
    {
        var now = DateTimeOffset.UtcNow;
        var accessToken = _tokenService.CreateNodeAccessToken(node.Id, node.Cue, AccessTokenLifetime);

        var plaintextRefreshToken = GenerateRefreshToken();
        var credential = new NodeCredential(
            Guid.NewGuid().ToString("N"),
            node.Id,
            HashRefreshToken(plaintextRefreshToken),
            now,
            now.Add(RefreshTokenLifetime),
            rotatedFrom,
            RevokedAt: null);

        _dbContext.NodeCredentials.Add(credential);

        return Task.FromResult(new IssuedCredentials(
            credential,
            accessToken,
            plaintextRefreshToken,
            now.Add(AccessTokenLifetime),
            credential.ExpiresAt));
    }

    /// <summary>
    /// Rotates a presented refresh token: the spent credential is revoked and a replacement is
    /// issued for the same node in a single SaveChangesAsync. Returns null when the token is
    /// unknown, already revoked or expired.
    /// </summary>
    public async Task<RotationResult?> RotateAsync(string presentedRefreshToken, CancellationToken cancellationToken)
    {
        var credential = await _dbContext.NodeCredentials
            .SingleOrDefaultAsync(
                candidate => candidate.RefreshTokenHash == HashRefreshToken(presentedRefreshToken),
                cancellationToken);

        if (credential is null || credential.RevokedAt is not null || credential.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var node = await _dbContext.RegisteredNodes
            .SingleAsync(candidate => candidate.Id == credential.NodeId, cancellationToken);

        _dbContext.Entry(credential).CurrentValues.SetValues(credential with { RevokedAt = DateTimeOffset.UtcNow });
        var issued = await IssueForNodeAsync(node, cancellationToken, rotatedFrom: credential.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RotationResult(node, issued);
    }

    /// <summary>
    /// Finds the node enrolled under the given (Cue, FingerprintHash) pair, or enrolls a new one.
    /// Identity IS that pair: a returning node is reused and never consumes another activation
    /// from the key. A new node increments the key's ActivationCount in the same SaveChangesAsync,
    /// so a crash can never desync the count from the node.
    /// </summary>
    public async Task<(RegisteredNode Node, bool IsNewNode)> FindOrEnrollAsync(
        ActivationKey key,
        string cue,
        string fingerprintHash,
        JsonDocument fingerprintComponents,
        string? appVersion,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.RegisteredNodes
            .Where(node => node.Cue == cue && node.FingerprintHash == fingerprintHash)
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return (existing, IsNewNode: false);
        }

        var now = DateTimeOffset.UtcNow;
        var node = new RegisteredNode(
            Guid.NewGuid().ToString("N"),
            SchoolId: null,
            Guid.NewGuid().ToString("N"),
            DeviceName: null,
            Status: "Active",
            LastSeenAt: null,
            now,
            now,
            fingerprintHash,
            fingerprintComponents,
            cue,
            key.Id,
            now,
            RevokedAt: null,
            appVersion);

        _dbContext.RegisteredNodes.Add(node);
        _dbContext.Entry(key).CurrentValues.SetValues(key with { ActivationCount = key.ActivationCount + 1 });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (node, IsNewNode: true);
    }

    private static string GenerateRefreshToken()
    {
        // 256 bits of CSPRNG entropy, base64url-encoded as the value handed to the caller.
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    // The hash covers the exact string the client presents back on refresh, so issue and rotation
    // agree without a decode round trip.
    private static string HashRefreshToken(string plaintextRefreshToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextRefreshToken))).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public sealed record IssuedCredentials(
        NodeCredential Credential,
        string AccessToken,
        string PlaintextRefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);

    public sealed record RotationResult(RegisteredNode Node, IssuedCredentials Credentials);
}
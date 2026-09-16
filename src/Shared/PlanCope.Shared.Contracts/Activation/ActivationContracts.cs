using System.Text.Json;

namespace PlanCope.Shared.Contracts.Activation;

/// <summary>
/// Redeems an activation key on behalf of a node. The node has no credential yet, so the key
/// itself is the proof of entitlement. Spanish localization is applied by the consumer (a
/// Local-side operator UI), never by these contracts.
/// </summary>
public sealed record ActivationRedeemRequest(
    string ActivationKey,
    string FingerprintHash,
    JsonDocument FingerprintComponents,
    string Cue,
    string? AppVersion);

/// <summary>
/// Why a redemption was refused. Each value maps to a distinct HTTP status in the controller
/// and is the typed vocabulary the operator UI renders. <see cref="FingerprintCollision"/> is
/// reserved for B2 (hardware identity) and is never produced by this batch: identity IS the
/// (Cue, FingerprintHash) pair, so a different fingerprint is a different node, not a collision.
/// </summary>
public enum ActivationRedeemFailureReason
{
    MalformedKey,
    KeyNotFound,
    KeyRevoked,
    KeyExpired,
    ActivationLimitReached,
    FingerprintCollision
}

/// <summary>
/// Discriminated result of a redemption attempt, mapped by the controller to distinct HTTP
/// statuses. Success carries the credential pair; failure carries exactly one typed reason.
/// </summary>
public sealed record ActivationRedeemResult(
    bool IsSuccess,
    ActivationRedeemFailureReason? Reason,
    ActivationRedeemResponse? Response)
{
    public static ActivationRedeemResult Succeeded(ActivationRedeemResponse response)
        => new(true, null, response);

    public static ActivationRedeemResult Failed(ActivationRedeemFailureReason reason)
        => new(false, reason, null);
}

/// <summary>
/// Successful redemption response. Every field is required and non-nullable: a missing field is
/// a deserialization error, never a silently null credential a caller could forward as real.
/// </summary>
public sealed record ActivationRedeemResponse
{
    public required string NodeId { get; init; }

    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAt { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }
}

public sealed record ActivationRefreshRequest(string RefreshToken);

/// <summary>
/// Response to a refresh rotation. Same required-everything rule as
/// <see cref="ActivationRedeemResponse"/>. <see cref="NodeRevoked"/> truthfully surfaces the
/// owning node's revocation state on this call — there is no second channel; the caller acts
/// on the flag, this endpoint only reports it.
/// </summary>
public sealed record ActivationRefreshResponse
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAt { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }

    public required bool NodeRevoked { get; init; }
}
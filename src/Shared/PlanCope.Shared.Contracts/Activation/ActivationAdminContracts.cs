namespace PlanCope.Shared.Contracts.Activation;

/// <summary>Issues a new activation key. Keys are universal: ScopeCue is always null and any key
/// can enrol a node at any CUE.</summary>
public sealed record IssueActivationKeyRequest(int MaxActivations, DateTimeOffset? ExpiresAt, string? Note);

/// <summary>Response of the issue endpoint. The plaintext key appears here and only here — this
/// is the one-time display of the key material.</summary>
public sealed record IssueActivationKeyResponse(
    string Id,
    string PlaintextKey,
    string KeyPrefix,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    int MaxActivations);

/// <summary>Key row for the admin list endpoint. Deliberately carries no key material: neither
/// the Argon2id hash nor any plaintext prefix beyond the display prefix.</summary>
public sealed record ActivationKeySummaryDto(
    string Id,
    string KeyPrefix,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    int MaxActivations,
    int ActivationCount,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    string? Note);

/// <summary>Body of the revoke-key endpoint. Stops future enrolments through the key; nodes
/// already enrolled by it keep working.</summary>
public sealed record RevokeActivationKeyRequest(string Reason);

/// <summary>Registered node row for the admin list endpoint.</summary>
public sealed record RegisteredNodeSummaryDto(
    string Id,
    string NodeCode,
    string Cue,
    string? DeviceName,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    string? SchoolName);

/// <summary>Body of the revoke-node endpoint. Stops only that machine; the key that enrolled it
/// is unaffected and can still be used elsewhere.</summary>
public sealed record RevokeNodeRequest(string Reason);
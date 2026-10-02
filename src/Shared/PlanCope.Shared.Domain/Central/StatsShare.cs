using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

/// <summary>Immutable public statistics snapshot. Only the token hash is stored.</summary>
public sealed record StatsShare(
    string Id,
    string TokenHash,
    string GroupBy,
    JsonDocument Filters,
    JsonDocument Snapshot,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string CreatedBy);

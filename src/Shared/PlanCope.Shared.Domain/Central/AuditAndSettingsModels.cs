using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

public sealed record AuditLog(string Id, string? ActorId, string EntityType, string EntityId, string Action, JsonDocument? Payload, string? IpAddress, DateTimeOffset CreatedAt);

public sealed record Setting(string Id, string Key, JsonDocument Value, string? UpdatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

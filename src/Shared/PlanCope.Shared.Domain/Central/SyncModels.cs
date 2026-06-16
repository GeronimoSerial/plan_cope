using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

public sealed record RegisteredNode(string Id, string? SchoolId, string NodeCode, string? DeviceName, string Status, DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CentralDeliverySession(string Id, string RemoteLocalId, string? SchoolId, string? ExamVersionId, string? ClassroomCode, string? CommissionCode, string Status, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, DateTimeOffset? SyncedAt, DateTimeOffset CreatedAt);

public sealed record ReceivedStudentAttempt(string Id, string RemoteLocalId, string? DeliverySessionId, string StudentCode, string Status, DateTimeOffset? StartedAt, DateTimeOffset? SubmittedAt, DateTimeOffset ReceivedAt, string IdempotencyKey, DateTimeOffset CreatedAt);

public sealed record ReceivedSubmissionAnswer(string Id, string StudentAttemptId, string BlockId, JsonDocument Answer, DateTimeOffset CreatedAt);

public sealed record SyncInbox(string Id, string? SourceNodeId, string EventType, string AggregateType, string AggregateId, string IdempotencyKey, JsonDocument Payload, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);

public sealed record SyncCursor(string Id, string NodeId, string CursorKey, string CursorValue, DateTimeOffset UpdatedAt);

public sealed record SyncAttempt(string Id, string? NodeId, string Direction, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, JsonDocument? Summary, JsonDocument? Error);

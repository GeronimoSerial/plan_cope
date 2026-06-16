using System.Text.Json;

namespace PlanCope.Shared.Contracts.Sync;

public sealed record PullRequest(string NodeId, string Cursor, int Limit);

public sealed record SyncItem(string EntityType, string EntityId, string Operation, JsonElement Payload, string UpdatedAt, string Checksum);

public sealed record PullResponse(IReadOnlyList<SyncItem> Items, string NextCursor, bool HasMore, IReadOnlyDictionary<string, string> Checksums);

public sealed record PushItem(string IdempotencyKey, string EventType, string AggregateType, string AggregateId, JsonElement Payload, string Checksum, string OccurredAt);

public sealed record PushRequest(string NodeId, IReadOnlyList<PushItem> Items);

public sealed record PushItemResult(string IdempotencyKey, string Status, string? Reason);

public sealed record PushResponse(int Received, int Failed, IReadOnlyList<PushItemResult> Results);

public static class SyncEventTypes
{
    public const string ExamPublished = "exam_published";
    public const string PackageCreated = "package_created";
    public const string NodeRegistered = "node_registered";
    public const string SessionCreated = "session_created";
    public const string SessionClosed = "session_closed";
    public const string SubmissionReceived = "submission_received";
    public const string AttemptSubmitted = "attempt_submitted";
    public const string AssetSynced = "asset_synced";
    public const string NodeHeartbeat = "node_heartbeat";
}

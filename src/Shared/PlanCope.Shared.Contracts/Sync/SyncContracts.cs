using System.Text.Json;

namespace PlanCope.Shared.Contracts.Sync;

public sealed record PullRequest(string NodeId, string Cursor, int Limit);

public sealed record SyncItem(string EntityType, string EntityId, string Operation, JsonElement Payload, string UpdatedAt, string Checksum);

public sealed record PullResponse(IReadOnlyList<SyncItem> Items, string NextCursor, bool HasMore, IReadOnlyDictionary<string, string> Checksums);

/// <summary>
/// Cursor keys persisted in Central's <c>sync.cursors</c> table. The exam-pull cursor tracks how
/// far a node has scanned published packages; the per-package delivery marker records that a
/// package was actually delivered to a node (used to compute <c>pulledByNodeCount</c>).
/// </summary>
public static class SyncCursorKeys
{
    public const string ExamPull = "exam_pull";
    public const string PackageDeliveryPrefix = "package:";

    public static string PackageDelivery(string packageId) => PackageDeliveryPrefix + packageId;
}

public sealed record PushItem(string IdempotencyKey, string EventType, string AggregateType, string AggregateId, JsonElement Payload, string Checksum, string OccurredAt);

public sealed record PushRequest(string NodeId, IReadOnlyList<PushItem> Items);

public sealed record PushItemResult(string IdempotencyKey, string Status, string? Reason);

public sealed record PushResponse(int Received, int Failed, IReadOnlyList<PushItemResult> Results);

public sealed record SessionHeartbeatRequest(
    string SessionId,
    string Cue,
    string? SchoolYear,
    string? RosterSectionId,
    string? ExamVersionId,
    string Status,
    int JoinedCount,
    int InProgressCount,
    int SubmittedCount,
    int ClosedOrForcedCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastActivityAt,
    string? AppVersion);

public sealed record LiveSessionSummary(
    string SessionId,
    string Cue,
    string? SchoolYear,
    string? RosterSectionId,
    string? ExamVersionId,
    string Status,
    int JoinedCount,
    int InProgressCount,
    int SubmittedCount,
    int ClosedOrForcedCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? LastHeartbeatAt,
    string SignalStatus,
    string? AppVersion);

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

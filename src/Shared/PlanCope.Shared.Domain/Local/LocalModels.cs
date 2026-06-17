namespace PlanCope.Shared.Domain.Local;

public sealed record LocalUser(string Id, string Username, string PasswordHash, string Role, bool Active, string? LastLoginAt, string CreatedAt);

public sealed record LocalExamVersion(string Id, string RemoteExamVersionId, string ExamCode, int VersionNumber, string Checksum, string? MetadataJson, int SchemaVersion, string SyncedAt);

public sealed record LocalExamBlock(string Id, string LocalExamVersionId, string RemoteBlockId, int OrderIndex, BlockType BlockType, string ConfigJson, string? ValidationJson);

public sealed record LocalAnswerKey(string Id, string LocalExamVersionId, string RemoteBlockId, string CorrectAnswerJson, double? ScoreValue);

public sealed record LocalAsset(string Id, string RemoteAssetId, string LocalExamVersionId, string FileName, string MimeType, string Checksum, string LocalPath, string SyncedAt);

public sealed record LocalDeliverySession(string Id, string ExamVersionId, string SchoolCode, string? ClassroomCode, string? CommissionCode, string StartedBy, string StartAt, string? EndAt, string Status, string? ConfigJson, string AccessCode, int ExpectedStudentCount);

public sealed record LocalSessionProgress(string SessionId, string AccessCode, int ExpectedStudentCount, int StartedCount, int SubmittedCount, int InProgressCount)
{
    public int CompletionPercentage => ExpectedStudentCount <= 0 ? 0 : Math.Min(100, (int)Math.Round(SubmittedCount * 100.0 / ExpectedStudentCount));
}

public sealed record StudentAttempt(string Id, string DeliverySessionId, string StudentCode, string Status, string StartedAt, string? SubmittedAt, int LocalSequence, string? ConfirmationCode);

public sealed record SubmissionAnswer(string Id, string StudentAttemptId, string BlockId, string AnswerJson, string CreatedAt);

public sealed record SyncOutbox(string Id, string EventType, string AggregateType, string AggregateId, string IdempotencyKey, string PayloadJson, string Status, int RetryCount, string? NextRetryAt, string? LastError, string CreatedAt, string? ProcessedAt);

public sealed record SyncState(string Id, string Key, string ValueJson, string UpdatedAt);

public sealed record LocalSyncAttempt(string Id, string Direction, string Status, string StartedAt, string? FinishedAt, string? SummaryJson, string? ErrorJson);

public sealed record LocalAuditLog(string Id, string? ActorId, string Action, string EntityType, string EntityId, string? PayloadJson, string CreatedAt);

public sealed record AppSetting(string Id, string Key, string ValueJson, string UpdatedAt);

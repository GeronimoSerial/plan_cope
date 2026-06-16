using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

public sealed record Exam(string Id, string Code, string Title, string? Description, string? Level, string? Area, string? Subject, string Status, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExamVersion(string Id, string ExamId, int VersionNumber, int SchemaVersion, string Status, JsonDocument? Metadata, string? CreatedBy, string? ReviewedBy, string? ApprovedBy, string? PublishedBy, DateTimeOffset? PublishedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExamBlock(string Id, string ExamVersionId, int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonDocument Config, JsonDocument? Validation, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExamBlockOption(string Id, string ExamBlockId, string Value, string Label, int OrderIndex, JsonDocument? Metadata, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AnswerKey(string Id, string ExamBlockId, JsonDocument CorrectAnswer, decimal? ScoreValue, JsonDocument? Metadata, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExamAsset(string Id, string ExamVersionId, string FileName, string MimeType, long SizeBytes, string Checksum, string StoragePath, DateTimeOffset CreatedAt);

public sealed record AssetUsage(string Id, string ExamBlockId, string ExamAssetId, string UsageType, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

using System.Text.Json;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Contracts.Exams;

public sealed record ExamSummaryDto(string Id, string Code, string Title, string? Level, string? Area, string? Subject, string Status, int VersionCount);

public sealed record ExamVersionDto(string Id, string ExamId, int VersionNumber, int SchemaVersion, string Status, JsonElement? Metadata, IReadOnlyList<BlockDto> Blocks, IReadOnlyList<AnswerKeyDto> AnswerKeys, IReadOnlyList<AssetDto> Assets);

public sealed record BlockDto(string Id, string VersionId, int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonElement Config, JsonElement? Validation);

public sealed record AnswerKeyDto(string Id, string BlockId, JsonElement CorrectAnswer, decimal? ScoreValue, JsonElement? Metadata);

public sealed record AssetDto(string Id, string VersionId, string FileName, string MimeType, long SizeBytes, string Checksum, string StoragePath);

public sealed record PublishedExamPackageDto(
    string PackageId,
    string ExamId,
    string ExamVersionId,
    string ExamCode,
    string Title,
    int VersionNumber,
    int SchemaVersion,
    string Checksum,
    JsonElement? Metadata,
    IReadOnlyList<BlockDto> Blocks,
    IReadOnlyList<AnswerKeyDto> AnswerKeys,
    IReadOnlyList<PublishedAssetDto> Assets,
    IReadOnlyList<PublicationTargetDto> Targets);

public sealed record PublishedAssetDto(string Id, string VersionId, string FileName, string MimeType, long SizeBytes, string Checksum, string ContentBase64);

public sealed record PublicationTargetDto(string TargetType, string? TargetId);

public sealed record CreateExamRequest(string Code, string Title, string? Description, string? Level, string? Area, string? Subject);

public sealed record CreateExamVersionRequest(int SchemaVersion, JsonElement? Metadata);

public sealed record UpsertBlockRequest(int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonElement Config, JsonElement? Validation);

public sealed record CreateAssetRequest(string FileName, string MimeType, string ContentBase64);

public sealed record PublishExamVersionRequest(string? Subject, string Grade, string? Division);

public sealed record PublishExamVersionResponse(string PackageId, string ExamVersionId, int PackageVersion, string Checksum, IReadOnlyList<PublicationTargetDto> Targets);

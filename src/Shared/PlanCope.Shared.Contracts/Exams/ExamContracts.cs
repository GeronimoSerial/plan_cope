using System.Text.Json;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Contracts.Exams;

public sealed record ExamSummaryDto(string Id, string Code, string Title, string? Level, string? Area, string? Subject, string Status, int VersionCount);

public sealed record ExamVersionDto(string Id, string ExamId, int VersionNumber, int SchemaVersion, string Status, JsonElement? Metadata, IReadOnlyList<BlockDto> Blocks, IReadOnlyList<AnswerKeyDto> AnswerKeys, IReadOnlyList<AssetDto> Assets);

public sealed record BlockDto(string Id, string VersionId, int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonElement Config, JsonElement? Validation);

public sealed record AnswerKeyDto(string Id, string BlockId, JsonElement CorrectAnswer, decimal? ScoreValue, JsonElement? Metadata);

public sealed record AssetDto(string Id, string VersionId, string FileName, string MimeType, long SizeBytes, string Checksum, string StoragePath);

public sealed record CreateExamRequest(string Code, string Title, string? Description, string? Level, string? Area, string? Subject);

public sealed record CreateExamVersionRequest(int SchemaVersion, JsonElement? Metadata);

public sealed record UpsertBlockRequest(int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonElement Config, JsonElement? Validation);

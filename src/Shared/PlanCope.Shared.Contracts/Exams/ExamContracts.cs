using System.Text.Json;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Contracts.Exams;

/// <summary>
/// Computed publication states for an exam. <c>draft</c> means no version can be published yet,
/// <c>ready_to_publish</c> means at least one version has blocks, and <c>published</c> means a
/// package was emitted for a version. Delivery to nodes is described by
/// <see cref="ExamSummaryDto.PulledByNodeCount"/> and the sync cursor, not by a fourth state.
/// </summary>
public static class ExamPublicationStates
{
    public const string Draft = "draft";
    public const string ReadyToPublish = "ready_to_publish";
    public const string Published = "published";
}

/// <summary>
/// Target type vocabulary shared by publish (writer) and sync pull (reader).
/// <c>grade</c>, <c>subject</c> and <c>division</c> are descriptive metadata: they describe the
/// audience of the exam but are NOT delivery filters. Only <c>node</c> and <c>school</c> filter
/// which nodes receive a package; see docs/central/exam-publishing-contract.md.
/// </summary>
public static class PublicationTargetTypes
{
    public const string Grade = "grade";
    public const string Subject = "subject";
    public const string Division = "division";
    public const string Node = "node";
    public const string School = "school";

    public static readonly IReadOnlyList<string> DeliveryFilterTypes = [Node, School];
}

public sealed record ExamSummaryDto(
    string Id,
    string Code,
    string Title,
    string? Level,
    string? Area,
    string? Subject,
    string Status,
    int VersionCount,
    string? InitialVersionId = null,
    string PublicationState = ExamPublicationStates.Draft,
    string? PublishedVersionId = null,
    int? PublishedVersionNumber = null,
    DateTimeOffset? PublishedAt = null,
    IReadOnlyList<PublicationTargetDto>? Targets = null,
    int? PulledByNodeCount = null);

public sealed record ExamVersionDto(
    string Id,
    string ExamId,
    int VersionNumber,
    int SchemaVersion,
    string Status,
    JsonElement? Metadata,
    IReadOnlyList<BlockDto> Blocks,
    IReadOnlyList<AnswerKeyDto> AnswerKeys,
    IReadOnlyList<AssetDto> Assets,
    string? ScoringPolicy,
    int BlockCount = 0,
    bool CanPublish = false,
    string? PublishBlockedReason = null,
    DateTimeOffset? PublishedAt = null,
    DateTimeOffset? SupersededAt = null,
    bool IsCurrent = false,
    int? BasedOnVersionNumber = null);

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
    IReadOnlyList<PublicationTargetDto> Targets,
    string? ScoringPolicy);

public sealed record PublishedAssetDto(string Id, string VersionId, string FileName, string MimeType, long SizeBytes, string Checksum, string ContentBase64);

public sealed record PublicationTargetDto(string TargetType, string? TargetId);

public sealed record CreateExamRequest(string Code, string Title, string? Description, string? Level, string? Area, string? Subject);

// Body is fully optional. Without sourceVersionId the new version is a deep copy of the exam's
// highest-numbered published version, falling back to the highest-numbered version overall when the
// exam has no published version; with sourceVersionId it copies that version. empty=true keeps the
// pre-versioning behaviour of creating a brand-new empty draft version. force=true lets the version
// be created even when the exam already has a draft (which is otherwise a 409 draft_exists).
public sealed record CreateExamVersionRequest(
    int? SchemaVersion = null,
    JsonElement? Metadata = null,
    string? ScoringPolicy = null,
    string? SourceVersionId = null,
    bool Empty = false,
    bool Force = false);

public sealed record UpdateExamRequest(string? Code, string Title, string? Description, string? Level, string? Area, string? Subject);

public sealed record UpsertBlockRequest(int OrderIndex, BlockType BlockType, string? Title, string? Description, JsonElement Config, JsonElement? Validation);

public sealed record CreateAssetRequest(string FileName, string MimeType, string ContentBase64);

public sealed record PublishExamVersionRequest(
    string? Subject,
    string Grade,
    string? Division,
    IReadOnlyList<string>? NodeIds = null,
    IReadOnlyList<string>? SchoolIds = null);

public sealed record PublishExamVersionResponse(string PackageId, string ExamVersionId, int PackageVersion, string Checksum, IReadOnlyList<PublicationTargetDto> Targets);

// Contrato canonico: reemplaza el documento completo de una version (bloques + answer keys) en una sola operacion.
public sealed record ReplaceExamDocumentRequest(JsonElement? Metadata, IReadOnlyList<DocumentBlockDto> Blocks, string? ScoringPolicy);

public sealed record DocumentBlockDto(
    int OrderIndex,
    BlockType BlockType,
    string? Title,
    string? Description,
    JsonElement Config,
    JsonElement? Validation,
    JsonElement? CorrectAnswer,
    decimal? ScoreValue);

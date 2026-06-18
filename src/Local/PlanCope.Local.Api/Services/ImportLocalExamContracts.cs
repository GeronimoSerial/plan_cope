using System.Text.Json;

namespace PlanCope.Local.Api.Services;

public sealed record ImportLocalExamRequest(
    string? Id,
    string ExamCode,
    string Title,
    string? Grade,
    string? Division,
    string? Subject,
    int? VersionNumber,
    IReadOnlyList<ImportLocalExamBlock>? Blocks,
    IReadOnlyList<ImportLocalExamAsset>? Assets);

public sealed record ImportLocalExamBlock(
    string? Id,
    string Type,
    string? Title,
    JsonElement Config,
    JsonElement? Validation,
    ImportLocalAnswerKey? AnswerKey);

public sealed record ImportLocalExamAsset(
    string? Id,
    string FileName,
    string MimeType,
    string? ContentBase64);

public sealed record ImportLocalAnswerKey(JsonElement CorrectAnswer, double? ScoreValue);

public sealed record ImportLocalExamResponse(string ExamId, string ExamCode, int VersionNumber, int BlockCount, int AssetCount);

public sealed record ImportLocalExamResult(ImportLocalExamResponse? Response, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Response is not null;

    public static ImportLocalExamResult Success(ImportLocalExamResponse response)
    {
        return new ImportLocalExamResult(response, []);
    }

    public static ImportLocalExamResult Failure(IReadOnlyList<string> errors)
    {
        return new ImportLocalExamResult(null, errors);
    }
}

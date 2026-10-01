using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlanCope.Shared.Domain;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed record BlockStatDto(
    string BlockId,
    int? OrderIndex,
    string? Title,
    int CorrectCount,
    int PartialCount,
    int IncorrectCount,
    int BlankCount,
    int UngradableCount);

public sealed record SchoolStatsDto(
    SuppressibleValue<int> AttemptCount,
    SuppressibleValue<double> AverageScorePercent);

public sealed record CourseStatsDto(
    string Course,
    SuppressibleValue<int> AttemptCount,
    SuppressibleValue<double> AverageScorePercent);

public sealed record ExamStatsDto(
    string ExamVersionId,
    string ExamCode,
    int VersionNumber,
    SuppressibleValue<int> AttemptCount,
    SuppressibleValue<double> AverageScorePercent,
    IReadOnlyList<BlockStatDto> Blocks,
    string? Title = null,
    IReadOnlyList<string>? Courses = null);

public sealed record StatsReportAttemptDto(
    string? StudentName,
    string? DocumentLast4,
    string Course,
    string Section,
    string ExamVersionId,
    string ExamCode,
    double? ScorePercent,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SubmittedAt);

public sealed record StatsReportDataDto(
    string Cue,
    string SchoolName,
    IReadOnlyList<StatsReportAttemptDto> Attempts,
    int DeliveredExamCount,
    int ExpectedStudentCount);

public sealed record StatsFilterOptionsDto(IReadOnlyList<string> SchoolYears, IReadOnlyList<string> Courses, IReadOnlyList<ExamFilterOptionDto> Exams);
public sealed class ExamFilterOptionDto
{
    public string ExamVersionId { get; set; } = string.Empty;
    public string ExamCode { get; set; } = string.Empty;
    public long VersionNumber { get; set; }
}

public interface IStatsQueryRepository
{
    Task<SchoolStatsDto> GetSchoolStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseStatsDto>> GetCourseStatsAsync(string cue, string rosterScope, string? schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExamStatsDto>> GetExamStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default);

    Task<StatsReportDataDto> GetReportDataAsync(string cue, string? schoolYear, string? course, string? examVersionId, CancellationToken cancellationToken = default);

    Task<bool> HasSubmittedAttemptsAsync(string cue, CancellationToken cancellationToken = default);

    Task<StatsFilterOptionsDto> GetReportFilterOptionsAsync(string cue, CancellationToken cancellationToken = default);
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlanCope.Shared.Domain;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed record BlockStatDto(
    string BlockId,
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
    IReadOnlyList<BlockStatDto> Blocks);

public interface IStatsQueryRepository
{
    Task<SchoolStatsDto> GetSchoolStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseStatsDto>> GetCourseStatsAsync(string cue, string rosterScope, string? schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExamStatsDto>> GetExamStatsAsync(string cue, string rosterScope, string? schoolYear, string? course, CancellationToken cancellationToken = default);
}
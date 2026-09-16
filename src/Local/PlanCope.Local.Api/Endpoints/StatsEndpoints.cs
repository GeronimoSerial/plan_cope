using System.Globalization;
using System.Text;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;

namespace PlanCope.Local.Api.Endpoints;

public static class StatsEndpoints
{
    private const string RosterScope = "school";
    private const string SuppressedLabel = "cohorte insuficiente";

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/stats/school", async Task<IResult> (
            string cue,
            string? schoolYear,
            string? course,
            IStatsQueryRepository statsQueryRepository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            var stats = await statsQueryRepository.GetSchoolStatsAsync(cue, RosterScope, schoolYear, course, cancellationToken);

            return Results.Ok(new
            {
                attemptCount = Render(stats.AttemptCount),
                averageScorePercent = Render(stats.AverageScorePercent)
            });
        });

        endpoints.MapGet("/api/stats/course", async Task<IResult> (
            string cue,
            string? schoolYear,
            IStatsQueryRepository statsQueryRepository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            var stats = await statsQueryRepository.GetCourseStatsAsync(cue, RosterScope, schoolYear, cancellationToken);

            return Results.Ok(stats.Select(course => new
            {
                course.Course,
                attemptCount = Render(course.AttemptCount),
                averageScorePercent = Render(course.AverageScorePercent)
            }).ToArray());
        });

        endpoints.MapGet("/api/stats/exam", async Task<IResult> (
            string cue,
            string? schoolYear,
            string? course,
            IStatsQueryRepository statsQueryRepository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            var stats = await statsQueryRepository.GetExamStatsAsync(cue, RosterScope, schoolYear, course, cancellationToken);

            return Results.Ok(stats.Select(exam => new
            {
                exam.ExamVersionId,
                exam.ExamCode,
                exam.VersionNumber,
                attemptCount = Render(exam.AttemptCount),
                averageScorePercent = Render(exam.AverageScorePercent),
                blocks = exam.Blocks.Select(block => new
                {
                    block.BlockId,
                    block.CorrectCount,
                    block.PartialCount,
                    block.IncorrectCount,
                    block.BlankCount,
                    block.UngradableCount
                }).ToArray()
            }).ToArray());
        });

        endpoints.MapGet("/api/stats/export.csv", async Task<IResult> (
            string cue,
            string? schoolYear,
            IStatsQueryRepository statsQueryRepository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            var stats = await statsQueryRepository.GetCourseStatsAsync(cue, RosterScope, schoolYear, cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("course,attempt_count,average_score_percent");
            foreach (var course in stats)
            {
                csv.Append(course.Course).Append(',');
                csv.Append(Render(course.AttemptCount)).Append(',');
                csv.Append(course.AverageScorePercent.IsSuppressed
                    ? SuppressedLabel
                    : course.AverageScorePercent.Value.ToString("F2", CultureInfo.InvariantCulture));
                csv.AppendLine();
            }

            return Results.Text(csv.ToString(), "text/csv; charset=utf-8");
        });

        return endpoints;
    }

    private static object Render(SuppressibleValue<int> value) =>
        value.IsSuppressed ? SuppressedLabel : value.Value;

    private static object Render(SuppressibleValue<double> value) =>
        value.IsSuppressed ? SuppressedLabel : value.Value;
}
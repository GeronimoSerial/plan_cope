using System.Text;
using System.Text.RegularExpressions;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;
using PlanCope.Local.Api.Services.Stats;

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
                course.Sections,
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
                exam.Title,
                exam.Courses,
                exam.Sections,
                exam.VersionNumber,
                attemptCount = Render(exam.AttemptCount),
                averageScorePercent = Render(exam.AverageScorePercent),
                blocks = exam.Blocks.Select(block => new
                {
                    block.BlockId,
                    block.OrderIndex,
                    block.Title,
                    block.CorrectCount,
                    block.PartialCount,
                    block.IncorrectCount,
                    block.BlankCount,
                    block.UngradableCount
                }).ToArray()
            }).ToArray());
        });

        endpoints.MapGet("/api/stats/filters", async Task<IResult> (
            string cue,
            IStatsQueryRepository statsQueryRepository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            return Results.Ok(await statsQueryRepository.GetReportFilterOptionsAsync(cue, cancellationToken));
        });

        endpoints.MapGet("/api/stats/report.html", async Task<IResult> (
            string cue,
            string? schoolYear,
            string? course,
            string? exam,
            IStatsQueryRepository statsQueryRepository,
            StatsHtmlReportBuilder reportBuilder,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(cue))
            {
                return Results.BadRequest(new { error = "cue es obligatorio." });
            }

            if (!await statsQueryRepository.HasSubmittedAttemptsAsync(cue, cancellationToken))
            {
                return Results.BadRequest(new { error = "La escuela todavía no tiene exámenes entregados en este equipo." });
            }

            var data = await statsQueryRepository.GetReportDataAsync(cue, schoolYear, course, exam, cancellationToken);
            var allExams = await statsQueryRepository.GetExamStatsAsync(cue, RosterScope, schoolYear, course, cancellationToken);
            var exams = string.IsNullOrWhiteSpace(exam)
                ? allExams
                : allExams.Where(item => item.ExamVersionId == exam).ToArray();
            var generatedAt = DateTimeOffset.UtcNow;
            var html = reportBuilder.Build(data, exams, schoolYear, course, exam, generatedAt, RosterScope);
            var filenameCue = Regex.Replace(cue, "[^A-Za-z0-9_-]", "_");
            var filename = $"informe-estadistico-{filenameCue}-{generatedAt:yyyyMMdd}.html";
            return Results.File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", filename);
        });

        endpoints.MapPost("/api/stats/rebuild", async Task<IResult> (
            IStatsRollupRepository statsRollupRepository,
            CancellationToken cancellationToken) =>
        {
            await statsRollupRepository.RebuildAllAsync(cancellationToken);
            return Results.Ok(new { message = "Estadisticas reconstruidas." });
        });

        return endpoints;
    }

    private static object Render(SuppressibleValue<int> value) =>
        value.IsSuppressed ? SuppressedLabel : value.Value;

    private static object Render(SuppressibleValue<double> value) =>
        value.IsSuppressed ? SuppressedLabel : value.Value;
}

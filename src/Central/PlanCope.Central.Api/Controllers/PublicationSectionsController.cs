using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize(Policy = "ExamAuthor")]
[Route("api/rosters")]
public sealed class PublicationSectionsController(PlanCopeDbContext dbContext, IConfiguration configuration) : ControllerBase
{
    private static readonly Regex CourseNumber = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpGet("publication-sections")]
    public async Task<ActionResult<IReadOnlyList<PublicationSectionOptionDto>>> GetOptions(
        [FromQuery] string[] grades,
        CancellationToken cancellationToken = default)
    {
        var requestedGrades = grades.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (requestedGrades.Any(grade => !ExamCourses.IsValid(grade)))
        {
            return BadRequest("grades contains an unsupported exam grade.");
        }

        if (requestedGrades.Count == 0)
        {
            return Ok(Array.Empty<PublicationSectionOptionDto>());
        }

        // Published exams go to every active node, so the section menu uses the latest
        // current-year roster available for each school represented by those nodes.
        var currentYear = AsistenciasRosterSource.ResolveSchoolYear(configuration);
        var targetCues = await dbContext.RegisteredNodes.AsNoTracking()
            .Where(node => node.Status == "Active" && node.RevokedAt == null)
            .Select(node => node.Cue)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (targetCues.Count == 0)
        {
            return Ok(Array.Empty<PublicationSectionOptionDto>());
        }

        var snapshots = await dbContext.GeRosterSnapshots.AsNoTracking()
            .Where(snapshot => targetCues.Contains(snapshot.Cue) && snapshot.SchoolYear == currentYear)
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .ToListAsync(cancellationToken);
        var latestSnapshotIds = snapshots
            .GroupBy(snapshot => snapshot.Cue, StringComparer.Ordinal)
            .Select(group => group.First().Id)
            .ToList();
        if (latestSnapshotIds.Count == 0)
        {
            return Ok(Array.Empty<PublicationSectionOptionDto>());
        }

        var rawSections = await dbContext.GeRosterSections.AsNoTracking()
            .Where(section => latestSnapshotIds.Contains(section.SnapshotId))
            .Select(section => new { section.Course, section.Division, section.Level, section.Shift })
            .ToListAsync(cancellationToken);

        var matchingSections = rawSections
            .Select(section => new
            {
                Grade = ResolveGrade(section.Course, section.Level),
                Division = section.Division?.Trim(),
                Shift = section.Shift?.Trim()
            })
            .Where(section => section.Grade is not null && requestedGrades.Contains(section.Grade) && !string.IsNullOrWhiteSpace(section.Division))
            .Select(section => new { Grade = section.Grade!, Division = section.Division!, section.Shift })
            .ToList();

        var options = new List<PublicationSectionOptionDto>();
        foreach (var gradeGroup in matchingSections.GroupBy(section => section.Grade, StringComparer.Ordinal))
        {
            foreach (var divisionGroup in gradeGroup.GroupBy(section => section.Division, StringComparer.OrdinalIgnoreCase))
            {
                var variants = divisionGroup
                    .GroupBy(section => section.Shift ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new { Shift = group.First().Shift, group.Key })
                    .ToList();
                var includeShiftInValue = variants.Count > 1;
                foreach (var variant in variants)
                {
                    var division = divisionGroup.First().Division;
                    var shift = includeShiftInValue ? variant.Shift ?? "Sin turno" : null;
                    var value = shift is null ? division : $"{division} · {shift}";
                    options.Add(new PublicationSectionOptionDto(gradeGroup.Key, value, division, shift));
                }
            }
        }

        return Ok(options
            .OrderBy(option => Array.IndexOf(ExamCourses.Labels.Keys.ToArray(), option.GradeValue))
            .ThenBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Shift, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static string? ResolveGrade(string? course, string? level)
    {
        if (string.IsNullOrWhiteSpace(course)) return null;
        var number = CourseNumber.Match(course);
        if (!number.Success || !int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var gradeNumber)) return null;

        var normalizedLevel = Normalize(level);
        var normalizedCourse = Normalize(course);
        var family = normalizedLevel.Contains("primar", StringComparison.Ordinal)
            ? "primaria"
            : normalizedLevel.Contains("secundar", StringComparison.Ordinal)
                ? "secundaria"
                : normalizedCourse.Contains("grado", StringComparison.Ordinal)
                    ? "primaria"
                    : normalizedCourse.Contains("ano", StringComparison.Ordinal)
                        ? "secundaria"
                        : null;
        var gradeValue = family is null ? null : $"{family}-{gradeNumber.ToString(CultureInfo.InvariantCulture)}";
        return ExamCourses.IsValid(gradeValue) ? gradeValue : null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
    }
}

public sealed record PublicationSectionOptionDto(string GradeValue, string Value, string Label, string? Shift);

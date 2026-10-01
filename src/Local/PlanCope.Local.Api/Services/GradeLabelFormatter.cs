namespace PlanCope.Local.Api.Services;

public static class GradeLabelFormatter
{
    public static string? Format(string? course, string? division, string? shift)
    {
        var grade = FormatCourse(course);
        var section = string.IsNullOrWhiteSpace(division) ? null : division.Trim();
        var classLabel = string.Join(" ", new[] { grade, section }.Where(static part => part is not null));
        var normalizedShift = string.IsNullOrWhiteSpace(shift) ? null : $"Turno {shift.Trim().ToLowerInvariant()}";
        var label = string.Join(" · ", new[] { classLabel, normalizedShift }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        return label.Length == 0 ? null : label;
    }

    private static string? FormatCourse(string? course)
    {
        if (string.IsNullOrWhiteSpace(course)) return null;
        var value = course.Trim();
        if (value.EndsWith('°') || value.EndsWith('º')) value = value[..^1].TrimEnd();
        return int.TryParse(value, out _) ? $"{value}°" : value;
    }
}

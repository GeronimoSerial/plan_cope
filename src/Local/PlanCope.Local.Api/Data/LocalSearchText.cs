using System.Globalization;
using System.Text;

namespace PlanCope.Local.Api.Data;

internal static class LocalSearchText
{
    private static readonly string[] Ordinals = ["", "primero", "segundo", "tercero", "cuarto", "quinto", "sexto", "septimo", "octavo", "noveno", "decimo", "undecimo", "duodecimo"];
    private static readonly HashSet<char> Separators = ['°', 'º', '.', '-', '_', '/', ',', '\'', '"', '“', '”', '‘', '’'];

    public static string? Fold(string? value)
    {
        if (value is null) return null;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var folded = new string(decomposed.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        var separated = new string(folded.Select(character => Separators.Contains(character) ? ' ' : character).ToArray());
        return string.Join(' ', separated.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<string> TokenizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var trimmed = query.Trim();
        if (trimmed.Length < 2) return [];
        return (Fold(trimmed.Length > 100 ? trimmed[..100] : trimmed) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(6)
            .ToArray();
    }

    public static string? Grade(string? course, string? division, string? shift)
    {
        var courseNumber = new string((course ?? "").Where(char.IsDigit).ToArray());
        var normalizedDivision = Fold(division) ?? "";
        var ordinal = int.TryParse(courseNumber, out var number) && number < Ordinals.Length ? Ordinals[number] : "";
        var compact = courseNumber + normalizedDivision.Replace(" ", "", StringComparison.Ordinal);
        var courseSuffix = courseNumber.Length > 0 ? $"{courseNumber}to" : "";
        return Fold($"{course} {division} {shift} {courseNumber} {courseSuffix} {ordinal} {compact}");
    }
}

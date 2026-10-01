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
        var words = (Fold(trimmed.Length > 100 ? trimmed[..100] : trimmed) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var tokens = new List<string>(Math.Min(words.Length, 6));
        for (var index = 0; index < words.Length && tokens.Count < 6; index++)
        {
            if (index + 1 < words.Length && words[index + 1].Length == 1 && char.IsLetter(words[index + 1][0])
                && TryCourseNumber(words[index], out var courseNumber))
            {
                tokens.Add(courseNumber + words[++index]);
                continue;
            }
            tokens.Add(words[index]);
        }
        return tokens;
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

    private static bool TryCourseNumber(string token, out string courseNumber)
    {
        if (token.Length is > 0 and <= 2 && token.All(char.IsDigit))
        {
            courseNumber = token;
            return true;
        }
        if (token.EndsWith("to", StringComparison.Ordinal) && token.Length is > 2 and <= 4 && token[..^2].All(char.IsDigit))
        {
            courseNumber = token[..^2];
            return true;
        }
        for (var number = 1; number < Ordinals.Length; number++)
        {
            if (token == Ordinals[number])
            {
                courseNumber = number.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }
        courseNumber = "";
        return false;
    }
}

using System.Globalization;
using System.Text;

namespace PlanCope.Local.Api.Data;

internal static class LocalSearchText
{
    private static readonly string[][] GradeAliases =
    [
        [], ["primero", "1ro", "1er"], ["segundo", "2do"], ["tercero", "3ro", "3er"],
        ["cuarto", "4to"], ["quinto", "5to"], ["sexto", "6to"], ["septimo", "7mo"],
        ["octavo", "8vo"], ["noveno", "9no"], ["decimo", "10mo"], ["undecimo", "11mo"], ["duodecimo", "12mo"]
    ];
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
        var aliases = int.TryParse(courseNumber, out var number) && number < GradeAliases.Length
            ? string.Join(' ', GradeAliases[number])
            : "";
        var compact = courseNumber + normalizedDivision.Replace(" ", "", StringComparison.Ordinal);
        return Fold($"{course} {division} {shift} {courseNumber} {aliases} {compact}");
    }

    private static bool TryCourseNumber(string token, out string courseNumber)
    {
        if (token.Length is > 0 and <= 2 && token.All(char.IsDigit))
        {
            courseNumber = token;
            return true;
        }
        for (var number = 1; number < GradeAliases.Length; number++)
        {
            if (GradeAliases[number].Contains(token, StringComparer.Ordinal))
            {
                courseNumber = number.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }
        courseNumber = "";
        return false;
    }
}

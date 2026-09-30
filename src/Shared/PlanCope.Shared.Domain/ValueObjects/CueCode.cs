using System.Globalization;

namespace PlanCope.Shared.Domain.ValueObjects;

public readonly record struct CueCode(string Value)
{
    public const int Length = 9;

    /// <summary>Converts Central's legacy seven-digit base CUE and newer full CUE storage to canonical form.</summary>
    public static bool TryFromSchool(long storedCue, int? annex, out string normalized)
    {
        if (storedCue < 0 || annex is < 0 or > 99)
        {
            normalized = string.Empty;
            return false;
        }
        var candidate = storedCue > 9_999_999
            ? storedCue.ToString("D9", CultureInfo.InvariantCulture)
            : $"{storedCue.ToString("D7", CultureInfo.InvariantCulture)}{(annex ?? 0).ToString("D2", CultureInfo.InvariantCulture)}";
        return TryNormalize(candidate, out normalized);
    }

    public static string FromSchool(long storedCue, int? annex)
    {
        if (!TryFromSchool(storedCue, annex, out var normalized))
            throw new ArgumentException("School row does not contain a valid CUE.", nameof(storedCue));
        return normalized;
    }

    public static bool IsValid(string? value) => TryNormalize(value, out _);

    public static bool TryNormalize(string? value, out string normalized)
    {
        if (value is null || value.Any(static character =>
                character is not (>= '0' and <= '9') && character != '-' && !char.IsWhiteSpace(character)))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = new string(value.Where(static character => character is >= '0' and <= '9').ToArray());

        if (normalized.Length != Length)
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    public static string Normalize(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException($"CUE must contain exactly {Length} digits.", nameof(value));
        }

        return normalized;
    }

    public static string ToGeApiFormat(string? value)
    {
        var normalized = Normalize(value);
        return $"{normalized[..7]}-{normalized[7..]}";
    }
}

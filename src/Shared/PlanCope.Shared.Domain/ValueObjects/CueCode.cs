namespace PlanCope.Shared.Domain.ValueObjects;

public readonly record struct CueCode(string Value)
{
    public const int Length = 9;

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

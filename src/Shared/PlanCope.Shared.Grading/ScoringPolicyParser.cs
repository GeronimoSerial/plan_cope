namespace PlanCope.Shared.Grading;

/// <summary>
/// Parses a scoring policy from its exact (case-insensitive) member name. Null, empty, whitespace,
/// numeric, or any other non-name input returns null — the parser never guesses a policy.
/// </summary>
public static class ScoringPolicyParser
{
    private static readonly string[] MemberNames =
    {
        nameof(ScoringPolicy.AllOrNothing),
        nameof(ScoringPolicy.ProportionalPenalised),
        nameof(ScoringPolicy.ProportionalPlain)
    };

    public static ScoringPolicy? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Enum.TryParse alone is unreliable here: its ignoreCase path rejects some lowercase
        // spellings, and it accepts numeric strings and comma-separated lists. Match against the
        // canonical member names exactly (case-insensitive) before trusting any parsed value.
        var trimmed = raw.Trim();
        foreach (var name in MemberNames)
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<ScoringPolicy>(name, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
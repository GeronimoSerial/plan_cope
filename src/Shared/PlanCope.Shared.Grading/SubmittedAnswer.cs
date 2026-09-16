namespace PlanCope.Shared.Grading;

/// <summary>
/// Minimal submitted-answer shape. A missing or empty submission represents a blank answer.
/// </summary>
public sealed record SubmittedAnswer
{
    public IReadOnlyList<string> SelectedOptionIds { get; init; } = Array.Empty<string>();

    public bool? SelectedBoolean { get; init; }

    public string? Text { get; init; }
}

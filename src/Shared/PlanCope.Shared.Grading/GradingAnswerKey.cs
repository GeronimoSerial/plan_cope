namespace PlanCope.Shared.Grading;

/// <summary>
/// Minimal answer-key shape for a gradable block. Callers map their own persisted
/// models into this type at the call site.
/// </summary>
public sealed record GradingAnswerKey
{
    public IReadOnlyList<string> CorrectOptionIds { get; init; } = Array.Empty<string>();

    public bool? CorrectBoolean { get; init; }

    public IReadOnlyList<string> AcceptedAnswers { get; init; } = Array.Empty<string>();
}

namespace PlanCope.Shared.Grading.Tests;

internal sealed record GradingFixture
{
    public ExamVersion ExamVersion { get; init; } = new();

    public Dictionary<string, SubmittedAnswer> Answers { get; init; } = new();

    public AttemptResult Expected { get; init; } = new();

    public ScoringPolicy? OverridePolicy { get; init; }
}
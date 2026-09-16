namespace PlanCope.Shared.Grading;

/// <summary>
/// Minimal exam-version shape consumed by the grading engine.
/// </summary>
public sealed record ExamVersion
{
    public string ExamVersionId { get; init; } = string.Empty;

    /// <summary>
    /// The exam's own declared scoring policy, when it has one.
    /// Legacy exams may declare none, in which case a caller-supplied override is required.
    /// </summary>
    public ScoringPolicy? DeclaredScoringPolicy { get; init; }

    public IReadOnlyList<GradableBlock> Blocks { get; init; } = Array.Empty<GradableBlock>();
}

namespace PlanCope.Shared.Grading;

/// <summary>
/// Minimal exam-version shape consumed by the grading engine.
/// </summary>
public sealed record ExamVersion
{
    public string ExamVersionId { get; init; } = string.Empty;

    public IReadOnlyList<GradableBlock> Blocks { get; init; } = Array.Empty<GradableBlock>();
}

namespace PlanCope.Shared.Grading;

/// <summary>
/// Result of grading a whole attempt. <see cref="Score"/> is the sum of all block scores;
/// <see cref="ScoreMax"/> is the sum of gradable block maxima only (ungradable blocks are
/// excluded from both numerator and denominator).
/// </summary>
public sealed record AttemptResult
{
    public string ExamVersionId { get; init; } = string.Empty;

    public int GradingSchemaVersion { get; init; } = Grading.GradingSchemaVersion.Current;

    public ScoringPolicy? ScoringPolicy { get; init; }

    public decimal Score { get; init; }

    public decimal ScoreMax { get; init; }

    public IReadOnlyList<BlockResult> Blocks { get; init; } = Array.Empty<BlockResult>();
}

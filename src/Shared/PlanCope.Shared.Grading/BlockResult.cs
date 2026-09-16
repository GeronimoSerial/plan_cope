using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Result of grading a single block.
/// </summary>
public sealed record BlockResult
{
    public string BlockId { get; init; } = string.Empty;

    public BlockType BlockType { get; init; }

    public BlockOutcome Outcome { get; init; }

    public decimal Score { get; init; }

    public decimal ScoreMax { get; init; }
}

using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Minimal gradable-block shape consumed by the grading engine.
/// </summary>
public sealed record GradableBlock
{
    public string BlockId { get; init; } = string.Empty;

    public BlockType Type { get; init; }

    public decimal ScoreMax { get; init; }

    public GradingAnswerKey AnswerKey { get; init; } = new();
}

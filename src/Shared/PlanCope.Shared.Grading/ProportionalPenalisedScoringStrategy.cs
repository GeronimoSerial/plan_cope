namespace PlanCope.Shared.Grading;

/// <summary>
/// (correctSelectedCount - incorrectSelectedCount) / totalCorrectCount, floored at 0.
/// </summary>
public sealed class ProportionalPenalisedScoringStrategy : MultiSelectScoringStrategyBase
{
    public static readonly ProportionalPenalisedScoringStrategy Instance = new();

    private ProportionalPenalisedScoringStrategy()
    {
    }

    protected override decimal Fraction(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount)
    {
        return (decimal)(correctSelectedCount - incorrectSelectedCount) / totalCorrectCount;
    }
}
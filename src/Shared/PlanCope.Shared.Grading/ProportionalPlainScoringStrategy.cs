namespace PlanCope.Shared.Grading;

/// <summary>
/// correctSelectedCount / totalCorrectCount. Selecting every option scores full marks by design.
/// </summary>
public sealed class ProportionalPlainScoringStrategy : MultiSelectScoringStrategyBase
{
    public static readonly ProportionalPlainScoringStrategy Instance = new();

    private ProportionalPlainScoringStrategy()
    {
    }

    protected override decimal Fraction(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount)
    {
        return (decimal)correctSelectedCount / totalCorrectCount;
    }
}
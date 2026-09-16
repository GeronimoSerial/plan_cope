namespace PlanCope.Shared.Grading;

/// <summary>
/// Full score only if the selected set exactly equals the correct set; otherwise zero.
/// This is the project's single "exact match" implementation.
/// </summary>
public sealed class AllOrNothingScoringStrategy : MultiSelectScoringStrategyBase
{
    public static readonly AllOrNothingScoringStrategy Instance = new();

    private AllOrNothingScoringStrategy()
    {
    }

    protected override decimal Fraction(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount)
    {
        return correctSelectedCount == totalCorrectCount && incorrectSelectedCount == 0 ? 1m : 0m;
    }
}
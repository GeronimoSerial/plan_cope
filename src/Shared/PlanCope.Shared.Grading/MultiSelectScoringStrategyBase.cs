namespace PlanCope.Shared.Grading;

/// <summary>
/// Shared guard rails for multi-select scoring strategies: degenerate inputs score zero and
/// the result is clamped to <c>[0, scoreMax]</c>. Each strategy supplies only its own fraction.
/// </summary>
public abstract class MultiSelectScoringStrategyBase : IMultiSelectScoringStrategy
{
    public decimal Score(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount, decimal scoreMax)
    {
        if (scoreMax <= 0m || totalCorrectCount <= 0)
        {
            return 0m;
        }

        var fraction = Fraction(correctSelectedCount, incorrectSelectedCount, totalCorrectCount);
        if (fraction < 0m)
        {
            fraction = 0m;
        }

        if (fraction > 1m)
        {
            fraction = 1m;
        }

        return Math.Round(fraction * scoreMax, 6, MidpointRounding.AwayFromZero);
    }

    protected abstract decimal Fraction(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount);
}
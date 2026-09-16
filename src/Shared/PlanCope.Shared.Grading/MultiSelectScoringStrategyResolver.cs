namespace PlanCope.Shared.Grading;

/// <summary>
/// Maps a <see cref="ScoringPolicy"/> to its single implementation.
/// </summary>
public sealed class MultiSelectScoringStrategyResolver
{
    public IMultiSelectScoringStrategy Resolve(ScoringPolicy policy)
    {
        return policy switch
        {
            ScoringPolicy.AllOrNothing => AllOrNothingScoringStrategy.Instance,
            ScoringPolicy.ProportionalPenalised => ProportionalPenalisedScoringStrategy.Instance,
            ScoringPolicy.ProportionalPlain => ProportionalPlainScoringStrategy.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown scoring policy.")
        };
    }
}
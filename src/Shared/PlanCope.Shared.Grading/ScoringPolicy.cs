namespace PlanCope.Shared.Grading;

/// <summary>
/// Determines how a multi-select (or multi-option) block is scored.
/// </summary>
public enum ScoringPolicy
{
    AllOrNothing = 0,
    ProportionalPenalised = 1,
    ProportionalPlain = 2
}

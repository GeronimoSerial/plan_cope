namespace PlanCope.Shared.Grading;

/// <summary>
/// Thrown when an exam cannot be graded because no scoring policy could be resolved.
/// The engine never guesses a policy.
/// </summary>
public sealed class UngradableExamException : Exception
{
    public UngradableExamException(string message)
        : base(message)
    {
    }
}

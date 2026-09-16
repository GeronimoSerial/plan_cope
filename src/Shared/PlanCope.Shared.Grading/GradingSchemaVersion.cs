namespace PlanCope.Shared.Grading;

/// <summary>
/// Version of the grading result schema. The value is an <see cref="int"/> constant
/// stamped onto every <see cref="AttemptResult"/> produced by <see cref="GradingEngine"/>.
/// </summary>
public static class GradingSchemaVersion
{
    public const int Current = 1;
}

namespace PlanCope.Shared.Grading;

/// <summary>
/// Distinguishable outcome of grading a single block.
/// <see cref="Blank"/> means no answer was submitted; <see cref="Incorrect"/> means an
/// answer was submitted and it was wrong. <see cref="Ungradable"/> blocks contribute to
/// neither the numerator nor the denominator of an attempt score.
/// </summary>
public enum BlockOutcome
{
    Correct = 0,
    Partial = 1,
    Incorrect = 2,
    Blank = 3,
    Ungradable = 4
}

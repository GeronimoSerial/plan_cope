namespace PlanCope.Shared.Grading;

/// <summary>
/// Scores a multi-select block given the overlap between the correct set and the selected set.
/// </summary>
public interface IMultiSelectScoringStrategy
{
    /// <summary>
    /// Computes the score for a block.
    /// </summary>
    /// <param name="correctSelectedCount">Number of selected options that are in the correct set.</param>
    /// <param name="incorrectSelectedCount">Number of selected options that are not in the correct set.</param>
    /// <param name="totalCorrectCount">Total number of options in the correct set.</param>
    /// <param name="scoreMax">Maximum achievable score for the block.</param>
    decimal Score(int correctSelectedCount, int incorrectSelectedCount, int totalCorrectCount, decimal scoreMax);
}
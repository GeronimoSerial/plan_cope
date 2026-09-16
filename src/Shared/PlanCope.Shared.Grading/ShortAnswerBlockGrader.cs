using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades short-answer blocks by normalised exact match: trim whitespace, compare case-insensitively,
/// any one of the configured accepted answers counts as correct. No accent folding or fuzzy matching.
/// </summary>
public sealed class ShortAnswerBlockGrader : IBlockGrader
{
    public BlockType BlockType => BlockType.ShortAnswer;

    public BlockResult Grade(GradableBlock block, SubmittedAnswer? answer, ScoringPolicy? policy)
    {
        var text = answer?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BlockResult
            {
                BlockId = block.BlockId,
                BlockType = BlockType.ShortAnswer,
                Outcome = BlockOutcome.Blank,
                Score = 0m,
                ScoreMax = block.ScoreMax
            };
        }

        var normalized = text.Trim();
        var isCorrect = block.AnswerKey.AcceptedAnswers.Any(
            accepted => string.Equals(accepted.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        return new BlockResult
        {
            BlockId = block.BlockId,
            BlockType = BlockType.ShortAnswer,
            Outcome = isCorrect ? BlockOutcome.Correct : BlockOutcome.Incorrect,
            Score = isCorrect ? block.ScoreMax : 0m,
            ScoreMax = block.ScoreMax
        };
    }
}
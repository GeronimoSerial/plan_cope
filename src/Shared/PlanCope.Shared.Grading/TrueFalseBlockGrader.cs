using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades true/false blocks as an all-or-nothing exact match over a two-option set, routed
/// through the same multi-select strategy mechanism used for multiple choice.
/// </summary>
public sealed class TrueFalseBlockGrader : IBlockGrader
{
    private const string TrueOptionId = "true";
    private const string FalseOptionId = "false";

    private readonly MultiSelectGrader _multiSelectGrader;

    public TrueFalseBlockGrader()
        : this(new MultiSelectGrader())
    {
    }

    public TrueFalseBlockGrader(MultiSelectGrader multiSelectGrader)
    {
        _multiSelectGrader = multiSelectGrader;
    }

    public BlockType BlockType => BlockType.TrueFalse;

    public BlockResult Grade(GradableBlock block, SubmittedAnswer? answer, ScoringPolicy? policy)
    {
        if (answer?.SelectedBoolean is null)
        {
            return new BlockResult
            {
                BlockId = block.BlockId,
                BlockType = BlockType.TrueFalse,
                Outcome = BlockOutcome.Blank,
                Score = 0m,
                ScoreMax = block.ScoreMax
            };
        }

        var correctId = block.AnswerKey.CorrectBoolean is true ? TrueOptionId : FalseOptionId;
        var selectedId = answer.SelectedBoolean.Value ? TrueOptionId : FalseOptionId;

        var synthesizedBlock = block with
        {
            AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { correctId } }
        };
        var synthesizedAnswer = new SubmittedAnswer { SelectedOptionIds = new[] { selectedId } };

        return _multiSelectGrader.Grade(synthesizedBlock, synthesizedAnswer, ScoringPolicy.AllOrNothing);
    }
}
using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class UngradableExclusionTests
{
    [Fact]
    public void Text_and_Image_blocks_are_excluded_from_both_numerator_and_denominator()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "exclusion",
            DeclaredScoringPolicy = ScoringPolicy.AllOrNothing,
            Blocks = new[]
            {
                new GradableBlock { BlockId = "text", Type = BlockType.Text, ScoreMax = 100 },
                new GradableBlock { BlockId = "image", Type = BlockType.Image, ScoreMax = 50 },
                new GradableBlock
                {
                    BlockId = "mc",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a" } }
                }
            }
        };

        var answers = new Dictionary<string, SubmittedAnswer>
        {
            ["mc"] = new() { SelectedOptionIds = new[] { "a" } }
        };

        var result = new GradingEngine().Grade(exam, answers);

        Assert.Equal(10m, result.Score);
        Assert.Equal(10m, result.ScoreMax);

        foreach (var blockResult in result.Blocks.Where(block => block.BlockType is BlockType.Text or BlockType.Image))
        {
            Assert.Equal(BlockOutcome.Ungradable, blockResult.Outcome);
            Assert.Equal(0m, blockResult.Score);
        }

        Assert.All(result.Blocks.Where(block => block.BlockType == BlockType.MultipleChoice),
            block => Assert.Equal(BlockOutcome.Correct, block.Outcome));
    }
}
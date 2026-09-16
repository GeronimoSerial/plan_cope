using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class BlankVsIncorrectTests
{
    [Fact]
    public void Blank_and_Incorrect_are_distinguishable_outcomes()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "blank-vs-incorrect",
            DeclaredScoringPolicy = ScoringPolicy.AllOrNothing,
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "blank",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a", "b" } }
                },
                new GradableBlock
                {
                    BlockId = "incorrect",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a", "b" } }
                }
            }
        };

        var answers = new Dictionary<string, SubmittedAnswer>
        {
            ["incorrect"] = new() { SelectedOptionIds = new[] { "c", "d" } }
        };

        var result = new GradingEngine().Grade(exam, answers);

        var blank = Assert.Single(result.Blocks, block => block.BlockId == "blank");
        var incorrect = Assert.Single(result.Blocks, block => block.BlockId == "incorrect");

        Assert.Equal(BlockOutcome.Blank, blank.Outcome);
        Assert.Equal(BlockOutcome.Incorrect, incorrect.Outcome);
        Assert.NotEqual(blank.Outcome, incorrect.Outcome);
        Assert.Equal(0m, blank.Score);
        Assert.Equal(0m, incorrect.Score);
    }
}
using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class MissingPolicyTests
{
    [Fact]
    public void Missing_multiple_choice_policy_defaults_to_all_or_nothing()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "no-policy",
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "mc", Type = BlockType.MultipleChoice, AllowsMultipleAnswers = true, ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a", "b" } }
                }
            }
        };
        var result = new GradingEngine().Grade(exam, new Dictionary<string, SubmittedAnswer>
        {
            ["mc"] = new() { SelectedOptionIds = new[] { "a" } }
        });
        Assert.Equal(0m, result.Score);
        Assert.Equal(BlockOutcome.Incorrect, Assert.Single(result.Blocks).Outcome);
    }

    [Fact]
    public void Each_multiple_choice_block_uses_its_own_policy()
    {
        var blocks = new[]
        {
            new GradableBlock { BlockId = "plain", Type = BlockType.MultipleChoice, AllowsMultipleAnswers = true, ScoringPolicy = ScoringPolicy.ProportionalPlain, ScoreMax = 6, AnswerKey = new() { CorrectOptionIds = new[] { "a", "b" } } },
            new GradableBlock { BlockId = "penalised", Type = BlockType.MultipleChoice, AllowsMultipleAnswers = true, ScoringPolicy = ScoringPolicy.ProportionalPenalised, ScoreMax = 6, AnswerKey = new() { CorrectOptionIds = new[] { "a", "b" } } }
        };
        var result = new GradingEngine().Grade(new ExamVersion { Blocks = blocks }, new Dictionary<string, SubmittedAnswer>
        {
            ["plain"] = new() { SelectedOptionIds = new[] { "a", "c" } },
            ["penalised"] = new() { SelectedOptionIds = new[] { "a", "c" } }
        });
        Assert.Equal(3m, result.Blocks.Single(block => block.BlockId == "plain").Score);
        Assert.Equal(0m, result.Blocks.Single(block => block.BlockId == "penalised").Score);
    }

    [Fact]
    public void Single_choice_ignores_a_configured_partial_policy()
    {
        var block = new GradableBlock
        {
            BlockId = "single", Type = BlockType.MultipleChoice, AllowsMultipleAnswers = false,
            ScoringPolicy = ScoringPolicy.ProportionalPlain, ScoreMax = 10,
            AnswerKey = new() { CorrectOptionIds = new[] { "a", "b" } }
        };
        var result = new GradingEngine().Grade(new ExamVersion { Blocks = new[] { block } }, new Dictionary<string, SubmittedAnswer>
        {
            ["single"] = new() { SelectedOptionIds = new[] { "a" } }
        });
        Assert.Equal(0m, result.Score);
    }
}

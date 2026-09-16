using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class MissingPolicyTests
{
    [Fact]
    public void Grade_throws_when_no_policy_resolves_for_a_multiple_choice_block()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "no-policy",
            DeclaredScoringPolicy = null,
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "mc",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a" } }
                }
            }
        };

        var exception = Assert.Throws<UngradableExamException>(
            () => new GradingEngine().Grade(exam, new Dictionary<string, SubmittedAnswer>()));

        Assert.Contains("no scoring policy resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Override_policy_rescues_an_exam_without_a_declared_policy()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "override-rescue",
            DeclaredScoringPolicy = null,
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "mc",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a" } }
                }
            }
        };

        var result = new GradingEngine().Grade(
            exam,
            new Dictionary<string, SubmittedAnswer> { ["mc"] = new() { SelectedOptionIds = new[] { "a" } } },
            overridePolicy: ScoringPolicy.AllOrNothing);

        Assert.Equal(ScoringPolicy.AllOrNothing, result.ScoringPolicy);
        Assert.Equal(10m, result.Score);
    }

    [Fact]
    public void Exam_without_multiple_choice_blocks_grades_without_a_policy()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "no-mc",
            DeclaredScoringPolicy = null,
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "tf",
                    Type = BlockType.TrueFalse,
                    ScoreMax = 5,
                    AnswerKey = new GradingAnswerKey { CorrectBoolean = true }
                }
            }
        };

        var result = new GradingEngine().Grade(
            exam,
            new Dictionary<string, SubmittedAnswer> { ["tf"] = new() { SelectedBoolean = true } });

        Assert.Equal(5m, result.Score);
        Assert.Null(result.ScoringPolicy);
    }
}
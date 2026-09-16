using Xunit;
using System.Text.Json;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class IdempotencyTests
{
    [Fact]
    public void Grading_the_same_input_twice_produces_an_identical_result()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "idempotent",
            DeclaredScoringPolicy = ScoringPolicy.ProportionalPenalised,
            Blocks = new[]
            {
                new GradableBlock
                {
                    BlockId = "mc",
                    Type = BlockType.MultipleChoice,
                    ScoreMax = 10,
                    AnswerKey = new GradingAnswerKey { CorrectOptionIds = new[] { "a", "b", "c" } }
                },
                new GradableBlock
                {
                    BlockId = "tf",
                    Type = BlockType.TrueFalse,
                    ScoreMax = 5,
                    AnswerKey = new GradingAnswerKey { CorrectBoolean = true }
                },
                new GradableBlock
                {
                    BlockId = "sa",
                    Type = BlockType.ShortAnswer,
                    ScoreMax = 7,
                    AnswerKey = new GradingAnswerKey { AcceptedAnswers = new[] { "París" } }
                },
                new GradableBlock { BlockId = "txt", Type = BlockType.Text, ScoreMax = 3 }
            }
        };

        var answers = new Dictionary<string, SubmittedAnswer>
        {
            ["mc"] = new() { SelectedOptionIds = new[] { "a", "z" } },
            ["tf"] = new() { SelectedBoolean = true },
            ["sa"] = new() { Text = "  parís " }
        };

        var engine = new GradingEngine();
        var first = engine.Grade(exam, answers);
        var second = engine.Grade(exam, answers);

        ResultAssert.Equal(first, second);
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
    }
}
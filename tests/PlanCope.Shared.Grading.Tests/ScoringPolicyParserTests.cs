using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class ScoringPolicyParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotARealPolicy")]
    [InlineData("0")]
    [InlineData("AllOrNothing,ProportionalPlain")]
    public void Parse_returns_null_for_inputs_that_are_not_an_exact_member_name(string? raw)
    {
        // "0" is the specific trap: Enum.TryParse<ScoringPolicy>("0", out var p) returns TRUE
        // with p == AllOrNothing (the zero member), because TryParse accepts the underlying
        // numeric value of any enum. A naive `if (TryParse(raw, out var p)) return p;` would
        // silently grade this as AllOrNothing instead of refusing. Parse must reject it.
        Assert.Null(ScoringPolicyParser.Parse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotARealPolicy")]
    [InlineData("0")]
    [InlineData("AllOrNothing,ProportionalPlain")]
    public void Every_string_the_parser_rejects_leaves_the_engine_unable_to_grade_a_multiple_choice_block(string? raw)
    {
        var resolved = ScoringPolicyParser.Parse(raw);
        Assert.Null(resolved);

        var exam = new ExamVersion
        {
            ExamVersionId = "malformed-policy",
            DeclaredScoringPolicy = resolved,
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

        Assert.Throws<UngradableExamException>(
            () => new GradingEngine().Grade(exam, new Dictionary<string, SubmittedAnswer>()));
    }

    [Fact]
    public void Parse_matches_member_names_case_insensitively()
    {
        Assert.Equal(ScoringPolicy.AllOrNothing, ScoringPolicyParser.Parse("allornothing"));
    }

    [Theory]
    [InlineData("AllOrNothing", ScoringPolicy.AllOrNothing)]
    [InlineData("ProportionalPenalised", ScoringPolicy.ProportionalPenalised)]
    [InlineData("ProportionalPlain", ScoringPolicy.ProportionalPlain)]
    public void Parse_returns_the_enum_value_for_an_exact_member_name(string raw, ScoringPolicy expected)
    {
        Assert.Equal(expected, ScoringPolicyParser.Parse(raw));
    }

    [Fact]
    public void Parse_rejects_numeric_values_that_are_not_member_names()
    {
        Assert.Null(ScoringPolicyParser.Parse("1"));
    }

    [Fact]
    public void Parse_rejects_comma_separated_member_name_lists()
    {
        Assert.Null(ScoringPolicyParser.Parse("AllOrNothing, ProportionalPlain"));
    }
}
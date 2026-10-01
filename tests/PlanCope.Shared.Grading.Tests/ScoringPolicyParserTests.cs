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
    public void Invalid_policy_config_maps_to_null_for_the_grading_default(string? raw)
    {
        using var configDocument = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            multiple = true,
            scoringPolicy = raw
        }));
        using var answerKey = System.Text.Json.JsonDocument.Parse("[\"a\",\"b\"]");
        var block = GradingJsonMapper.MapBlock("mc", BlockType.MultipleChoice, 10m, answerKey.RootElement, configDocument.RootElement);
        Assert.Null(block.ScoringPolicy);
        Assert.True(block.AllowsMultipleAnswers);

        var result = new GradingEngine().Grade(new ExamVersion { Blocks = new[] { block } }, new Dictionary<string, SubmittedAnswer>
        {
            ["mc"] = new() { SelectedOptionIds = new[] { "a" } }
        });
        Assert.Equal(0m, result.Score);
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

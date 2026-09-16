using Xunit;

namespace PlanCope.Shared.Grading.Tests;

public sealed class ScoringPolicyParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotARealPolicy")]
    public void Parse_returns_null_for_inputs_that_are_not_an_exact_member_name(string? raw)
    {
        Assert.Null(ScoringPolicyParser.Parse(raw));
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
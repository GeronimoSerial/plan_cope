using Xunit;
using System.Text.Json;

namespace PlanCope.Shared.Grading.Tests;

public sealed class GoldenFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TheoryData<string> FixtureFileNames => new()
    {
        { "multiple-choice.json" },
        { "true-false.json" },
        { "short-answer.json" },
        { "policy-all-or-nothing.json" },
        { "policy-proportional-penalised.json" },
        { "policy-proportional-plain.json" }
    };

    [Theory]
    [MemberData(nameof(FixtureFileNames))]
    public void Grade_matches_golden_fixture(string fixtureFileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "GoldenFixtures", fixtureFileName);
        var json = File.ReadAllText(fixturePath);
        var fixture = JsonSerializer.Deserialize<GradingFixture>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize fixture '{fixtureFileName}'.");

        var engine = new GradingEngine();
        var actual = engine.Grade(fixture.ExamVersion, fixture.Answers, fixture.OverridePolicy);

        ResultAssert.Equal(fixture.Expected, actual);
    }
}
using Xunit;

namespace PlanCope.Shared.Grading.Tests;

internal static class ResultAssert
{
    public static void Equal(AttemptResult expected, AttemptResult actual)
    {
        Assert.Equal(expected.ExamVersionId, actual.ExamVersionId);
        Assert.Equal(expected.GradingSchemaVersion, actual.GradingSchemaVersion);
        Assert.Equal(expected.ScoringPolicy, actual.ScoringPolicy);
        Assert.Equal(expected.Score, actual.Score);
        Assert.Equal(expected.ScoreMax, actual.ScoreMax);
        Assert.Equal(expected.Blocks.Count, actual.Blocks.Count);

        for (var i = 0; i < expected.Blocks.Count; i++)
        {
            Assert.Equal(expected.Blocks[i], actual.Blocks[i]);
        }
    }
}
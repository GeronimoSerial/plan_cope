using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class UngradableExclusionTests
{
    [Fact]
    public void Removed_block_types_are_rejected_by_the_grading_engine()
    {
        var exam = new ExamVersion
        {
            ExamVersionId = "exclusion",
            DeclaredScoringPolicy = ScoringPolicy.AllOrNothing,
            Blocks = new[] { new GradableBlock { BlockId = "removed", Type = (BlockType)0, ScoreMax = 1 } }
        };

        Assert.Throws<UngradableExamException>(() => new GradingEngine().Grade(exam, null));
    }
}

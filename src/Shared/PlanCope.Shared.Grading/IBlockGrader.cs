using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades a single block of a specific <see cref="BlockType"/>.
/// </summary>
public interface IBlockGrader
{
    BlockType BlockType { get; }

    BlockResult Grade(GradableBlock block, SubmittedAnswer? answer, ScoringPolicy? policy);
}
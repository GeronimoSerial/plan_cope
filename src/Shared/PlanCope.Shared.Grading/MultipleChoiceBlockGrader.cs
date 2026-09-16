using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades multiple-choice blocks through the multi-select grader under the resolved policy.
/// </summary>
public sealed class MultipleChoiceBlockGrader : IBlockGrader
{
    private readonly MultiSelectGrader _multiSelectGrader;

    public MultipleChoiceBlockGrader()
        : this(new MultiSelectGrader())
    {
    }

    public MultipleChoiceBlockGrader(MultiSelectGrader multiSelectGrader)
    {
        _multiSelectGrader = multiSelectGrader;
    }

    public BlockType BlockType => BlockType.MultipleChoice;

    public BlockResult Grade(GradableBlock block, SubmittedAnswer? answer, ScoringPolicy? policy)
    {
        return _multiSelectGrader.Grade(block, answer, policy);
    }
}
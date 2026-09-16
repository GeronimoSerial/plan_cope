using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades multi-select blocks (multiple choice and, by synthesis, true/false) by resolving
/// the scoring strategy for the given policy and delegating to it. Contains no per-policy
/// branching itself.
/// </summary>
public sealed class MultiSelectGrader
{
    private readonly MultiSelectScoringStrategyResolver _resolver;

    public MultiSelectGrader()
        : this(new MultiSelectScoringStrategyResolver())
    {
    }

    public MultiSelectGrader(MultiSelectScoringStrategyResolver resolver)
    {
        _resolver = resolver;
    }

    public BlockResult Grade(GradableBlock block, SubmittedAnswer? answer, ScoringPolicy? policy)
    {
        if (policy is null)
        {
            throw new UngradableExamException($"No scoring policy resolved for block '{block.BlockId}'.");
        }

        var selected = Normalize(answer?.SelectedOptionIds);
        if (selected.Count == 0)
        {
            return new BlockResult
            {
                BlockId = block.BlockId,
                BlockType = block.Type,
                Outcome = BlockOutcome.Blank,
                Score = 0m,
                ScoreMax = block.ScoreMax
            };
        }

        var correct = Normalize(block.AnswerKey.CorrectOptionIds);
        var correctSelected = selected.Count(option => correct.Contains(option));
        var incorrectSelected = selected.Count - correctSelected;
        var strategy = _resolver.Resolve(policy.Value);
        var score = strategy.Score(correctSelected, incorrectSelected, correct.Count, block.ScoreMax);
        var outcome = score == block.ScoreMax && block.ScoreMax > 0m
            ? BlockOutcome.Correct
            : score > 0m
                ? BlockOutcome.Partial
                : BlockOutcome.Incorrect;

        return new BlockResult
        {
            BlockId = block.BlockId,
            BlockType = block.Type,
            Outcome = outcome,
            Score = score,
            ScoreMax = block.ScoreMax
        };
    }

    private static IReadOnlyCollection<string> Normalize(IReadOnlyList<string>? optionIds)
    {
        if (optionIds is null)
        {
            return Array.Empty<string>();
        }

        return optionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
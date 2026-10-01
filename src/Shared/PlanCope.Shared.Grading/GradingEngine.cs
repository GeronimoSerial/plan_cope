using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Grades a whole attempt. Pure and deterministic: no I/O, no clock, no randomness.
/// </summary>
public sealed class GradingEngine
{
    private readonly IReadOnlyDictionary<BlockType, IBlockGrader> _graders;

    public GradingEngine()
        : this(DefaultGraders())
    {
    }

    public GradingEngine(IEnumerable<IBlockGrader> graders)
    {
        _graders = graders.ToDictionary(grader => grader.BlockType);
    }

    /// <summary>
    /// Grades every block of <paramref name="examVersion"/> against <paramref name="answers"/>
    /// (keyed by block id; absent keys are blank). Each block carries its scoring policy.
    /// </summary>
    public AttemptResult Grade(
        ExamVersion examVersion,
        IReadOnlyDictionary<string, SubmittedAnswer>? answers)
    {
        var results = new List<BlockResult>(examVersion.Blocks.Count);
        foreach (var block in examVersion.Blocks)
        {
            results.Add(GradeBlock(block, answers));
        }

        var score = results.Sum(result => result.Score);
        var scoreMax = results
            .Where(result => result.Outcome != BlockOutcome.Ungradable)
            .Sum(result => result.ScoreMax);
        var effectivePolicies = examVersion.Blocks
            .Select(static block => block.Type == BlockType.MultipleChoice && block.AllowsMultipleAnswers
                ? block.ScoringPolicy ?? ScoringPolicy.AllOrNothing
                : ScoringPolicy.AllOrNothing)
            .Distinct()
            .ToList();

        return new AttemptResult
        {
            ExamVersionId = examVersion.ExamVersionId,
            GradingSchemaVersion = GradingSchemaVersion.Current,
            // The legacy attempt summary is retained only when every block used the same rule.
            // Mixed-policy attempts are described by their block results and have no scalar summary.
            ScoringPolicy = effectivePolicies.Count == 1 ? effectivePolicies[0] : null,
            Score = score,
            ScoreMax = scoreMax,
            Blocks = results
        };
    }

    private BlockResult GradeBlock(
        GradableBlock block,
        IReadOnlyDictionary<string, SubmittedAnswer>? answers)
    {
        if (!_graders.TryGetValue(block.Type, out var grader))
        {
            throw new UngradableExamException($"No grader registered for block type '{block.Type}'.");
        }

        SubmittedAnswer? submitted = null;
        if (answers is not null && answers.TryGetValue(block.BlockId, out var found))
        {
            submitted = found;
        }

        return grader.Grade(block, submitted);
    }

    private static IEnumerable<IBlockGrader> DefaultGraders()
    {
        return new IBlockGrader[]
        {
            new MultipleChoiceBlockGrader(),
            new TrueFalseBlockGrader()
        };
    }
}

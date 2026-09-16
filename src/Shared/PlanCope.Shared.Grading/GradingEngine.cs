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
    /// (keyed by block id; absent keys are blank). The effective policy is the caller-supplied
    /// override when present, otherwise the exam's declared policy. If an exam contains a
    /// multiple-choice block and no policy resolves, an <see cref="UngradableExamException"/>
    /// is thrown — the engine never guesses a policy.
    /// </summary>
    public AttemptResult Grade(
        ExamVersion examVersion,
        IReadOnlyDictionary<string, SubmittedAnswer>? answers,
        ScoringPolicy? overridePolicy = null)
    {
        var policy = overridePolicy ?? examVersion.DeclaredScoringPolicy;
        var hasMultipleChoice = examVersion.Blocks.Any(block => block.Type == BlockType.MultipleChoice);
        if (hasMultipleChoice && policy is null)
        {
            throw new UngradableExamException(
                $"No scoring policy resolved for exam version '{examVersion.ExamVersionId}'; " +
                "cannot grade its multiple-choice blocks.");
        }

        var results = new List<BlockResult>(examVersion.Blocks.Count);
        foreach (var block in examVersion.Blocks)
        {
            results.Add(GradeBlock(block, answers, policy));
        }

        var score = results.Sum(result => result.Score);
        var scoreMax = results
            .Where(result => result.Outcome != BlockOutcome.Ungradable)
            .Sum(result => result.ScoreMax);

        return new AttemptResult
        {
            ExamVersionId = examVersion.ExamVersionId,
            GradingSchemaVersion = GradingSchemaVersion.Current,
            ScoringPolicy = policy,
            Score = score,
            ScoreMax = scoreMax,
            Blocks = results
        };
    }

    private BlockResult GradeBlock(
        GradableBlock block,
        IReadOnlyDictionary<string, SubmittedAnswer>? answers,
        ScoringPolicy? policy)
    {
        if (block.Type is BlockType.Text or BlockType.Image)
        {
            return new BlockResult
            {
                BlockId = block.BlockId,
                BlockType = block.Type,
                Outcome = BlockOutcome.Ungradable,
                Score = 0m,
                ScoreMax = block.ScoreMax
            };
        }

        if (!_graders.TryGetValue(block.Type, out var grader))
        {
            throw new UngradableExamException($"No grader registered for block type '{block.Type}'.");
        }

        SubmittedAnswer? submitted = null;
        if (answers is not null && answers.TryGetValue(block.BlockId, out var found))
        {
            submitted = found;
        }

        return grader.Grade(block, submitted, policy);
    }

    private static IEnumerable<IBlockGrader> DefaultGraders()
    {
        return new IBlockGrader[]
        {
            new MultipleChoiceBlockGrader(),
            new TrueFalseBlockGrader(),
            new ShortAnswerBlockGrader()
        };
    }
}
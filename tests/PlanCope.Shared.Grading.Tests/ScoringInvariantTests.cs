using Xunit;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading.Tests;

public sealed class ScoringInvariantTests
{
    private const int Seed = 20260214;
    private const int Iterations = 500;

    private static readonly string[] OptionPool = { "a", "b", "c", "d", "e" };
    private static readonly string[] WordPool = { "apple", "pear", "lemon", "grape", "plum" };
    private static readonly BlockType[] AllBlockTypes = { BlockType.Text, BlockType.Image, BlockType.MultipleChoice, BlockType.TrueFalse, BlockType.ShortAnswer };
    private static readonly ScoringPolicy[] Policies = { ScoringPolicy.AllOrNothing, ScoringPolicy.ProportionalPenalised, ScoringPolicy.ProportionalPlain };

    [Fact]
    public void Block_scores_are_within_range_and_sum_to_attempt_score()
    {
        var random = new Random(Seed);
        var engine = new GradingEngine();

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var (exam, answers) = BuildRandomAttempt(random);
            var result = engine.Grade(exam, answers);

            Assert.Equal(result.Blocks.Sum(block => block.Score), result.Score);
            Assert.InRange(result.Score, 0m, result.ScoreMax);

            foreach (var blockResult in result.Blocks.Where(block => block.Outcome != BlockOutcome.Ungradable))
            {
                Assert.InRange(blockResult.Score, 0m, blockResult.ScoreMax);
            }
        }
    }

    private static (ExamVersion Exam, Dictionary<string, SubmittedAnswer> Answers) BuildRandomAttempt(Random random)
    {
        var blockCount = random.Next(1, 7);
        var blocks = new List<GradableBlock>(blockCount);
        var answers = new Dictionary<string, SubmittedAnswer>();

        for (var i = 0; i < blockCount; i++)
        {
            var blockId = $"b{i}";
            var type = AllBlockTypes[random.Next(AllBlockTypes.Length)];
            var scoreMax = (decimal)random.Next(0, 11);
            var block = new GradableBlock { BlockId = blockId, Type = type, ScoreMax = scoreMax };

            switch (type)
            {
                case BlockType.MultipleChoice:
                    block = block with { AnswerKey = new GradingAnswerKey { CorrectOptionIds = RandomSubset(random) } };
                    if (random.Next(4) > 0)
                    {
                        answers[blockId] = new SubmittedAnswer { SelectedOptionIds = RandomSubset(random) };
                    }

                    break;

                case BlockType.TrueFalse:
                    block = block with { AnswerKey = new GradingAnswerKey { CorrectBoolean = random.Next(2) == 1 } };
                    if (random.Next(4) > 0)
                    {
                        answers[blockId] = new SubmittedAnswer { SelectedBoolean = random.Next(2) == 1 };
                    }

                    break;

                case BlockType.ShortAnswer:
                    block = block with { AnswerKey = new GradingAnswerKey { AcceptedAnswers = RandomWordSubset(random) } };
                    if (random.Next(4) > 0)
                    {
                        var word = random.Next(2) == 0 ? RandomWord(random) : WordPool[random.Next(WordPool.Length)];
                        answers[blockId] = new SubmittedAnswer { Text = MutateText(random, word) };
                    }

                    break;

                case BlockType.Text:
                case BlockType.Image:
                    break;
            }

            blocks.Add(block);
        }

        var exam = new ExamVersion
        {
            ExamVersionId = $"random-{random.Next()}",
            DeclaredScoringPolicy = Policies[random.Next(Policies.Length)],
            Blocks = blocks
        };

        return (exam, answers);
    }

    private static IReadOnlyList<string> RandomSubset(Random random)
    {
        return OptionPool
            .Where(_ => random.Next(2) == 1)
            .ToList();
    }

    private static IReadOnlyList<string> RandomWordSubset(Random random)
    {
        return WordPool
            .Where(_ => random.Next(2) == 1)
            .ToList();
    }

    private static string RandomWord(Random random)
    {
        var letters = "abcdefghijklmnopqrstuvwxyz";
        var length = random.Next(1, 8);
        return new string(Enumerable.Repeat(letters, length).Select(_ => letters[random.Next(letters.Length)]).ToArray());
    }

    private static string MutateText(Random random, string word)
    {
        var mutated = word.ToUpperInvariant();
        if (random.Next(2) == 0)
        {
            mutated = " " + mutated + "  ";
        }

        return mutated;
    }
}
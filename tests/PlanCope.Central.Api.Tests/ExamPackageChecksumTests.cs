using System.Text.Json;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ExamPackageChecksumTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static Exam MakeExam() => new("ex-1", "EXA-2026-01", "Matematica", null, [], null, null, "Approved", null, Now, Now);
    private static ExamVersion MakeVersion() => new("ev-1", "ex-1", 2, 1, "Approved", null, null, null, null, null, null, Now, Now);
    private static IReadOnlyList<BlockDto> MakeBlocks(string policy) => new[]
    {
        new BlockDto("blk-1", "ev-1", 0, BlockType.MultipleChoice, "Pregunta", null,
            JsonDocument.Parse(JsonSerializer.Serialize(new { multiple = true, scoringPolicy = policy })).RootElement.Clone(), null)
    };

    [Fact]
    public void Compute_Differs_WhenQuestionScoringPolicyChanges()
    {
        var plain = ExamPackageChecksum.Compute(MakeExam(), MakeVersion(), MakeBlocks("ProportionalPlain"), [], [], [], null);
        var penalised = ExamPackageChecksum.Compute(MakeExam(), MakeVersion(), MakeBlocks("ProportionalPenalised"), [], [], [], null);
        Assert.NotEqual(plain, penalised);
    }

    [Fact]
    public void Compute_IsStable_ForIdenticalVersions()
    {
        var first = ExamPackageChecksum.Compute(MakeExam(), MakeVersion(), MakeBlocks("AllOrNothing"), [], [], [], null);
        var second = ExamPackageChecksum.Compute(MakeExam(), MakeVersion(), MakeBlocks("AllOrNothing"), [], [], [], null);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_ContinuesToIncludeStoredTargetsInLegacyPackageChecksums()
    {
        var withNodeTarget = ExamPackageChecksum.Compute(
            MakeExam(), MakeVersion(), MakeBlocks("AllOrNothing"), [],
            [], [new PublicationTargetDto("grade", "primaria-6")], null);
        var withDifferentNodeTarget = ExamPackageChecksum.Compute(
            MakeExam(), MakeVersion(), MakeBlocks("AllOrNothing"), [],
            [], [new PublicationTargetDto("grade", "secundaria-1")], null);

        Assert.NotEqual(withNodeTarget, withDifferentNodeTarget);
    }

    private static JsonElement JsonElementOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

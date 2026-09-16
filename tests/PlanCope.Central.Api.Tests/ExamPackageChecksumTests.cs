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

    private static Exam MakeExam()
    {
        return new Exam(
            "ex-1",
            "EXA-2026-01",
            "Matematica · Primer Año",
            null,
            "Secundario",
            "Matematica",
            "Numeros y Operaciones",
            "Approved",
            null,
            Now,
            Now);
    }

    private static ExamVersion MakeVersion(string? scoringPolicy)
    {
        return new ExamVersion(
            "ev-1",
            "ex-1",
            2,
            1,
            "Approved",
            null,
            null,
            null,
            null,
            null,
            null,
            Now,
            Now,
            scoringPolicy);
    }

    private static IReadOnlyList<BlockDto> MakeBlocks()
    {
        return new[]
        {
            new BlockDto(
                "blk-1",
                "ev-1",
                0,
                BlockType.MultipleChoice,
                "Pregunta 1",
                "Cuanto es 2 + 2?",
                JsonElementOf("""{"options":["A","B","C","D"],"answerIndex":1}"""),
                null),
        };
    }

    [Fact]
    public void Compute_Differs_WhenOnlyScoringPolicyChanges()
    {
        var withPolicy = ExamPackageChecksum.Compute(MakeExam(), MakeVersion("ProportionalPenalised"), MakeBlocks(), Array.Empty<AnswerKeyDto>(), Array.Empty<PublishedAssetDto>(), Array.Empty<PublicationTargetDto>(), null);
        var withoutPolicy = ExamPackageChecksum.Compute(MakeExam(), MakeVersion("Proportional"), MakeBlocks(), Array.Empty<AnswerKeyDto>(), Array.Empty<PublishedAssetDto>(), Array.Empty<PublicationTargetDto>(), null);

        Assert.NotEqual(withPolicy, withoutPolicy);
    }

    [Fact]
    public void Compute_IsStable_ForIdenticalVersions()
    {
        var first = ExamPackageChecksum.Compute(MakeExam(), MakeVersion("ProportionalPenalised"), MakeBlocks(), Array.Empty<AnswerKeyDto>(), Array.Empty<PublishedAssetDto>(), Array.Empty<PublicationTargetDto>(), null);
        var second = ExamPackageChecksum.Compute(MakeExam(), MakeVersion("ProportionalPenalised"), MakeBlocks(), Array.Empty<AnswerKeyDto>(), Array.Empty<PublishedAssetDto>(), Array.Empty<PublicationTargetDto>(), null);

        Assert.Equal(first, second);
    }

    private static JsonElement JsonElementOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
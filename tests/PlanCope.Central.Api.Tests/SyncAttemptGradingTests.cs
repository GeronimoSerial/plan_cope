using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using PlanCope.TestSupport;
using Xunit;
using GradingSchemaVersion = PlanCope.Shared.Grading.GradingSchemaVersion;

namespace PlanCope.Central.Api.Tests;

public sealed class SyncAttemptGradingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Attempt_with_examVersionRemoteId_is_graded_from_central_definition()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-1");
        var version = MakeVersion("ev-1", exam.Id, "AllOrNothing");
        var block = MakeBlock("blk-1", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("ak-1", block.Id, """["B"]""", 1m));
        await dbContext.SaveChangesAsync();

        const string attemptId = "attempt-1";
        var response = await PushAsync(dbContext, CreateAttemptItem(
            "key-1",
            attemptId,
            version.Id,
            new[] { MakeAnswerPayload(attemptId, block.Id, """["B"]""") }));

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);

        var received = await dbContext.ReceivedStudentAttempts.SingleAsync(x => x.RemoteLocalId == attemptId);
        var result = await dbContext.CentralAttemptResults.SingleAsync(x => x.ReceivedStudentAttemptId == received.Id);
        Assert.Equal("graded", result.Status);
        Assert.Equal(1m, result.Score);
        Assert.Equal(1m, result.ScoreMax);
        Assert.Equal("AllOrNothing", result.ScoringPolicy);
        Assert.Equal(GradingSchemaVersion.Current, result.GradingSchemaVersion);
        Assert.NotNull(result.BlocksJson);
        var blockResult = Assert.Single(result.BlocksJson!.RootElement.EnumerateArray().ToList());
        Assert.Equal("blk-1", blockResult.GetProperty("BlockId").GetString());
        Assert.Equal("Correct", blockResult.GetProperty("Outcome").GetString());
        Assert.Equal(1m, blockResult.GetProperty("Score").GetDecimal());
    }

    [Fact]
    public async Task Attempt_against_no_policy_exam_is_accepted_and_marked_ungradable()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-2");
        var version = MakeVersion("ev-2", exam.Id, null);
        var block = MakeBlock("blk-2", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("ak-2", block.Id, """["B"]""", 1m));
        await dbContext.SaveChangesAsync();

        const string attemptId = "attempt-2";
        var response = await PushAsync(dbContext, CreateAttemptItem(
            "key-2",
            attemptId,
            version.Id,
            new[] { MakeAnswerPayload(attemptId, block.Id, """["B"]""") }));

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);

        var received = await dbContext.ReceivedStudentAttempts.SingleAsync(x => x.RemoteLocalId == attemptId);
        var result = await dbContext.CentralAttemptResults.SingleAsync(x => x.ReceivedStudentAttemptId == received.Id);
        Assert.Equal("ungradable", result.Status);
        Assert.Null(result.Score);
        Assert.Null(result.ScoreMax);
        Assert.Null(result.ScoringPolicy);
    }

    [Fact]
    public async Task Attempt_without_examVersionRemoteId_is_accepted_without_grading()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-3");
        var version = MakeVersion("ev-3", exam.Id, "AllOrNothing");
        var block = MakeBlock("blk-3", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("ak-3", block.Id, """["B"]""", 1m));
        await dbContext.SaveChangesAsync();

        const string attemptId = "attempt-3";
        var response = await PushAsync(dbContext, CreateAttemptItem(
            "key-3",
            attemptId,
            examVersionRemoteId: null,
            new[] { MakeAnswerPayload(attemptId, block.Id, """["B"]""") }));

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);
        Assert.Equal(1, await dbContext.ReceivedStudentAttempts.CountAsync());
        Assert.Equal(1, await dbContext.ReceivedSubmissionAnswers.CountAsync());
        Assert.Equal(0, await dbContext.CentralAttemptResults.CountAsync());
    }

    [Fact]
    public async Task Attempt_with_unknown_policy_string_is_ungradable_never_all_or_nothing()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-4");
        var version = MakeVersion("ev-4", exam.Id, "NotARealPolicy");
        var block = MakeBlock("blk-4", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("ak-4", block.Id, """["B"]""", 1m));
        await dbContext.SaveChangesAsync();

        const string attemptId = "attempt-4";
        var response = await PushAsync(dbContext, CreateAttemptItem(
            "key-4",
            attemptId,
            version.Id,
            new[] { MakeAnswerPayload(attemptId, block.Id, """["B"]""") }));

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);

        var received = await dbContext.ReceivedStudentAttempts.SingleAsync(x => x.RemoteLocalId == attemptId);
        var result = await dbContext.CentralAttemptResults.SingleAsync(x => x.ReceivedStudentAttemptId == received.Id);
        Assert.Equal("ungradable", result.Status);
        Assert.Null(result.Score);
        Assert.Null(result.ScoreMax);
        Assert.Null(result.ScoringPolicy);
    }

    [Fact]
    public async Task Attempt_against_no_policy_exam_with_assignment_is_graded_using_assigned_policy()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-5");
        var version = MakeVersion("ev-5", exam.Id, null);
        var block = MakeBlock("blk-5", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("ak-5", block.Id, """["B"]""", 1m));
        dbContext.GradingPolicyAssignments.Add(new GradingPolicyAssignment(
            "gpa-1",
            version.Id,
            "AllOrNothing",
            "admin-1",
            Now,
            null));
        await dbContext.SaveChangesAsync();

        const string attemptId = "attempt-5";
        var response = await PushAsync(dbContext, CreateAttemptItem(
            "key-5",
            attemptId,
            version.Id,
            new[] { MakeAnswerPayload(attemptId, block.Id, """["B"]""") }));

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);

        var received = await dbContext.ReceivedStudentAttempts.SingleAsync(x => x.RemoteLocalId == attemptId);
        var result = await dbContext.CentralAttemptResults.SingleAsync(x => x.ReceivedStudentAttemptId == received.Id);
        Assert.Equal("graded", result.Status);
        Assert.Equal(1m, result.Score);
        Assert.Equal(1m, result.ScoreMax);
        Assert.Equal("AllOrNothing", result.ScoringPolicy);
        Assert.Equal(GradingSchemaVersion.Current, result.GradingSchemaVersion);
        Assert.NotNull(result.BlocksJson);
    }

    private static void SeedExam(
        PlanCopeDbContext dbContext,
        Exam exam,
        ExamVersion version,
        ExamBlock block,
        AnswerKey answerKey)
    {
        dbContext.Exams.Add(exam);
        dbContext.ExamVersions.Add(version);
        dbContext.ExamBlocks.Add(block);
        dbContext.AnswerKeys.Add(answerKey);
    }

    private static async Task<PushResponse> PushAsync(PlanCopeDbContext dbContext, PushItem item)
    {
        var request = new PushRequest("node-1", new[] { item });
        var controller = new SyncController(dbContext, new CentralStatsRollupService(dbContext));
        var result = await controller.Push(request, "node-1", new PushRequestValidator(), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PushResponse>(okResult.Value);
    }

    private static PushItem CreateAttemptItem(
        string idempotencyKey,
        string attemptId,
        string? examVersionRemoteId,
        IReadOnlyList<object> answers)
    {
        var payloadObject = new Dictionary<string, object?>
        {
            ["attempt"] = new
            {
                id = attemptId,
                deliverySessionId = "session-1",
                studentCode = "GE:42",
                status = "submitted",
                startedAt = "2026-08-20T12:00:00+00:00",
                submittedAt = "2026-08-20T12:05:00+00:00",
                localSequence = 1,
                confirmationCode = "ABC123"
            },
            ["answers"] = answers
        };
        if (examVersionRemoteId is not null)
        {
            payloadObject["examVersionRemoteId"] = examVersionRemoteId;
        }

        var payload = JsonSerializer.SerializeToElement(payloadObject);
        return new PushItem(
            idempotencyKey,
            SyncEventTypes.AttemptSubmitted,
            "student_attempt",
            attemptId,
            payload,
            SyncPayloadChecksum.Calculate(payload),
            "2026-08-20T12:05:00+00:00");
    }

    private static object MakeAnswerPayload(string attemptId, string blockId, string answerJson)
    {
        return new
        {
            id = $"ans-{Guid.NewGuid():N}",
            studentAttemptId = attemptId,
            blockId,
            answerJson,
            createdAt = "2026-08-20T12:04:00+00:00"
        };
    }

    private static Exam MakeExam(string id)
    {
        return new Exam(
            id,
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

    private static ExamVersion MakeVersion(string id, string examId, string? scoringPolicy)
    {
        return new ExamVersion(
            id,
            examId,
            1,
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

    private static ExamBlock MakeBlock(string id, string versionId)
    {
        return new ExamBlock(
            id,
            versionId,
            0,
            BlockType.MultipleChoice,
            "Pregunta 1",
            "Cuanto es 18 + 24?",
            JsonDocument.Parse("""{"options":["A","B"]}"""),
            null,
            Now,
            Now);
    }

    private static AnswerKey MakeAnswerKey(string id, string blockId, string correctAnswerJson, decimal? scoreValue)
    {
        return new AnswerKey(
            id,
            blockId,
            JsonDocument.Parse(correctAnswerJson),
            scoreValue,
            null,
            Now,
            Now);
    }

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PlanCopeDbContext(options);
    }
}
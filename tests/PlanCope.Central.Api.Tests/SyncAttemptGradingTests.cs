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
        var version = MakeVersion("ev-1", exam.Id);
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
    public async Task Push_with_session_block_creates_session_and_rollup_and_duplicate_is_idempotent()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("session-exam");
        var version = MakeVersion("session-version", exam.Id);
        var block = MakeBlock("session-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("session-key", block.Id, "[\"B\"]", 1m));
        SeedRoster(dbContext, "session-section");
        await dbContext.SaveChangesAsync();

        var item = CreateAttemptItem("session-key-1", "session-attempt", version.Id,
            new[] { MakeAnswerPayload("session-attempt", block.Id, "[\"B\"]") },
            new { id = "session-1", schoolCue = "180000101", schoolYear = "2026", course = "6to A", sectionId = "session-section", examVersionId = version.Id, startedAt = Now.ToString("O"), closedAt = Now.ToString("O"), status = "closed" },
            "session-section");
        var first = await PushAsync(dbContext, item);
        var duplicate = await PushAsync(dbContext, item);
        var updatedSessionItem = CreateAttemptItem("session-key-2", "session-attempt-2", null,
            Array.Empty<object>(),
            new { id = "session-1", schoolCue = (string?)null, schoolYear = "2026", course = "6to A", sectionId = "session-section", examVersionId = (string?)null, startedAt = Now.AddMinutes(1).ToString("O"), closedAt = (string?)null, status = "active" },
            "session-section");
        var update = await PushAsync(dbContext, updatedSessionItem);

        Assert.Equal("accepted", first.Results.Single().Status);
        Assert.Equal("duplicate", duplicate.Results.Single().Status);
        Assert.Equal("accepted", update.Results.Single().Status);
        var session = await dbContext.DeliverySessions.SingleAsync();
        Assert.Equal("180000101", session.SchoolId);
        Assert.Equal(version.Id, session.ExamVersionId);
        Assert.Equal(Now, session.EndedAt);
        Assert.Equal("closed", session.Status);
        Assert.Equal("node-1", session.SourceNodeId);
        Assert.Equal(2, await dbContext.ReceivedStudentAttempts.CountAsync());
        var rollup = await dbContext.ExamRollups.SingleAsync();
        Assert.Equal(1, rollup.AttemptCount);
        Assert.Equal("6to A", rollup.Course);
        Assert.Equal("180000100", rollup.Cue);
    }

    [Fact]
    public async Task Session_ids_are_scoped_to_the_owning_node()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("owned-session-exam");
        var version = MakeVersion("owned-session-version", exam.Id);
        var block = MakeBlock("owned-session-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("owned-session-key", block.Id, "[\"B\"]", 1m));
        SeedRoster(dbContext, "owned-session-section");
        await dbContext.SaveChangesAsync();

        var first = CreateAttemptItem("owner-one-key", "owner-one-attempt", null, Array.Empty<object>(),
            new { id = "shared-local-session", schoolCue = "180000100", examVersionId = version.Id, closedAt = Now.ToString("O"), status = "closed" });
        var second = CreateAttemptItem("owner-two-key", "owner-two-attempt", null, Array.Empty<object>(),
            new { id = "shared-local-session", schoolCue = "180000101", examVersionId = (string?)null, closedAt = (string?)null, status = "active" });

        Assert.Equal("accepted", (await PushAsync(dbContext, first, "node-one")).Results.Single().Status);
        Assert.Equal("accepted", (await PushAsync(dbContext, second, "node-two")).Results.Single().Status);

        var sessions = await dbContext.DeliverySessions.OrderBy(session => session.SourceNodeId).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(new[] { "node-one", "node-two" }, sessions.Select(session => session.SourceNodeId));
        Assert.Equal("180000100", sessions[0].SchoolId);
        Assert.Equal("180000101", sessions[1].SchoolId);
        Assert.Null(sessions[1].ExamVersionId);
        Assert.Equal("closed", sessions[0].Status);
    }

    [Fact]
    public async Task Legacy_push_without_session_block_uses_roster_section_snapshot_cue()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("legacy-exam");
        var version = MakeVersion("legacy-version", exam.Id);
        var block = MakeBlock("legacy-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("legacy-key", block.Id, "[\"B\"]", 1m));
        SeedRoster(dbContext, "legacy-section");
        await dbContext.SaveChangesAsync();

        var item = CreateAttemptItem("legacy-push-key", "legacy-attempt", version.Id,
            new[] { MakeAnswerPayload("legacy-attempt", block.Id, "[\"B\"]") }, rosterSectionId: "legacy-section");
        var response = await PushAsync(dbContext, item);

        Assert.Equal("accepted", response.Results.Single().Status);
        var rollup = await dbContext.ExamRollups.SingleAsync();
        Assert.Equal("180000100", rollup.Cue);
        Assert.Equal("2026", rollup.SchoolYear);
        Assert.Equal("6to A", rollup.Course);
    }

    [Fact]
    public async Task Session_cue_is_used_when_roster_cue_is_invalid_and_cue_is_known()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("known-cue-exam");
        var version = MakeVersion("known-cue-version", exam.Id);
        var block = MakeBlock("known-cue-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("known-cue-key", block.Id, "[\"B\"]", 1m));
        SeedRoster(dbContext, "invalid-cue-section", "not-a-cue");
        dbContext.GeRosterSnapshots.Add(new GeRosterSnapshot
        {
            Id = "known-cue-snapshot", Cue = "180000101", SchoolYear = "2026", FetchedAt = Now,
            Checksum = "known", SectionCount = 0, StudentCount = 0, Status = "current"
        });
        await dbContext.SaveChangesAsync();

        var item = CreateAttemptItem("known-cue-key-1", "known-cue-attempt", version.Id,
            new[] { MakeAnswerPayload("known-cue-attempt", block.Id, "[\"B\"]") },
            new { id = "known-cue-session", schoolCue = "180000101", examVersionId = version.Id },
            "invalid-cue-section");
        var response = await PushAsync(dbContext, item);

        Assert.Equal("accepted", response.Results.Single().Status);
        Assert.Equal("180000101", (await dbContext.ExamRollups.SingleAsync()).Cue);
    }

    [Fact]
    public async Task Rollup_rebuild_reproduces_existing_aggregates()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("rebuild-exam");
        var version = MakeVersion("rebuild-version", exam.Id);
        var block = MakeBlock("rebuild-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("rebuild-key", block.Id, "[\"B\"]", 1m));
        SeedRoster(dbContext, "rebuild-section");
        await dbContext.SaveChangesAsync();
        var item = CreateAttemptItem("rebuild-push-key", "rebuild-attempt", version.Id,
            new[] { MakeAnswerPayload("rebuild-attempt", block.Id, "[\"B\"]") }, rosterSectionId: "rebuild-section");
        await PushAsync(dbContext, item);
        dbContext.SyncInbox.Add(new SyncInbox(
            "duplicate-attempt-inbox", "another-node", SyncEventTypes.AttemptSubmitted, "student_attempt",
            "rebuild-attempt", "duplicate-aggregate-key",
            JsonDocument.Parse("""{"examVersionRemoteId":"wrong-version"}"""), "processed", Now, Now));
        await dbContext.SaveChangesAsync();

        var before = await dbContext.ExamRollups.AsNoTracking().SingleAsync();
        var rebuilt = await new CentralStatsRollupService(dbContext).RebuildAllAsync();
        var after = await dbContext.ExamRollups.AsNoTracking().SingleAsync();

        Assert.Equal(1, rebuilt);
        Assert.Equal(before.Cue, after.Cue);
        Assert.Equal(before.SchoolYear, after.SchoolYear);
        Assert.Equal(before.Course, after.Course);
        Assert.Equal(before.ExamVersionId, after.ExamVersionId);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.ScoreSum, after.ScoreSum);
        Assert.Equal(before.ScoreMaxSum, after.ScoreMaxSum);
        var blockAfter = await dbContext.ExamRollupBlocks.SingleAsync();
        Assert.Equal(1, blockAfter.CorrectCount);
    }

    [Fact]
    public async Task Attempt_without_exam_level_policy_uses_question_default()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-2");
        var version = MakeVersion("ev-2", exam.Id);
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
        Assert.Equal("graded", result.Status);
        Assert.Equal(1m, result.Score);
        Assert.Equal(1m, result.ScoreMax);
    }

    [Fact]
    public async Task Attempt_without_examVersionRemoteId_is_accepted_without_grading()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("ex-3");
        var version = MakeVersion("ev-3", exam.Id);
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
    public async Task Off_roster_attempt_identity_is_accepted_and_stored()
    {
        using var dbContext = CreateDbContext();
        const string attemptId = "attempt-off-roster";
        var original = CreateAttemptItem("key-off-roster", attemptId, examVersionRemoteId: null, Array.Empty<object>());
        var payloadNode = System.Text.Json.Nodes.JsonNode.Parse(original.Payload.GetRawText())!;
        var attempt = payloadNode["attempt"]!;
        attempt["documentHmac"] = new string('a', 64);
        attempt["documentLast4"] = "5432";
        attempt["studentFirstName"] = "Bruno";
        attempt["studentLastName"] = "Díaz";
        attempt["offRoster"] = true;
        var payload = JsonSerializer.SerializeToElement(payloadNode);
        var item = original with { Payload = payload, Checksum = SyncPayloadChecksum.Calculate(payload) };

        var response = await PushAsync(dbContext, item);

        Assert.Equal(1, response.Received);
        Assert.Equal("accepted", response.Results[0].Status);
        var received = await dbContext.ReceivedStudentAttempts.SingleAsync(x => x.RemoteLocalId == attemptId);
        Assert.Equal(new string('a', 64), received.DocumentHmac);
        Assert.Equal("5432", received.DocumentLast4);
        Assert.Equal("Bruno", received.StudentFirstName);
        Assert.Equal("Díaz", received.StudentLastName);
        Assert.True(received.OffRoster);
        Assert.Null(received.RosterStudentId);
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

    private static async Task<PushResponse> PushAsync(PlanCopeDbContext dbContext, PushItem item, string nodeId = "node-1")
    {
        var request = new PushRequest(nodeId, new[] { item });
        var controller = new SyncController(dbContext, new CentralStatsRollupService(dbContext));
        SyncTestPrincipals.BindNode(controller, nodeId);
        var result = await controller.Push(request, nodeId, new PushRequestValidator(), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PushResponse>(okResult.Value);
    }

    private static void SeedRoster(PlanCopeDbContext dbContext, string sectionId, string cue = "180000100")
    {
        dbContext.GeRosterSnapshots.Add(new GeRosterSnapshot
        {
            Id = $"snapshot-{sectionId}", Cue = cue, SchoolYear = "2026", FetchedAt = Now,
            Checksum = "test", SectionCount = 1, StudentCount = 1, Status = "current"
        });
        dbContext.GeRosterSections.Add(new GeRosterSection
        {
            Id = sectionId, SnapshotId = $"snapshot-{sectionId}", Course = "6to A"
        });
    }

    private static PushItem CreateAttemptItem(
        string idempotencyKey,
        string attemptId,
        string? examVersionRemoteId,
        IReadOnlyList<object> answers,
        object? deliverySession = null,
        string? rosterSectionId = null)
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
        if (deliverySession is not null) payloadObject["deliverySession"] = deliverySession;
        if (rosterSectionId is not null) payloadObject["rosterSectionId"] = rosterSectionId;

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
            ["secundaria-1"],
            "Matematica",
            "Numeros y Operaciones",
            "Approved",
            null,
            Now,
            Now);
    }

    private static ExamVersion MakeVersion(string id, string examId)
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
            Now);
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

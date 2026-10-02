using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Auth;
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
        await AssertStatsContainsCueAsync(dbContext, "180000100", "2026", "6to A");
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
    public async Task Normal_session_without_roster_attributes_attempt_and_shows_it_in_stats()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("no-roster-exam");
        var version = MakeVersion("no-roster-version", exam.Id);
        var block = MakeBlock("no-roster-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("no-roster-key", block.Id, "[\"B\"]", 1m));
        dbContext.Schools.Add(new School("school-1", "CUE-180000100", 180000100, null, "Escuela de prueba", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();

        var item = CreateAttemptItem("no-roster-idem", "no-roster-attempt", version.Id,
            new[] { MakeAnswerPayload("no-roster-attempt", block.Id, "[\"B\"]") },
            new { id = "no-roster-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", sectionId = (string?)null, examVersionId = version.Id, startedAt = Now.ToString("O"), closedAt = Now.ToString("O"), status = "closed" });

        var response = await PushAsync(dbContext, item);
        Assert.Equal("accepted", response.Results.Single().Status);
        var rollup = await dbContext.ExamRollups.SingleAsync();
        Assert.Equal("180000100", rollup.Cue);
        Assert.Equal("2026", rollup.SchoolYear);
        Assert.Equal("6to A", rollup.Course);
        Assert.Equal("attributed", (await dbContext.ReceivedStudentAttempts.SingleAsync()).AttributionStatus);

        using var scope = CreateAuthorizationScope();
        var stats = new StatsController(dbContext, scope.ServiceProvider.GetRequiredService<IAuthorizationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roster_scope", "province")], "test"))
                }
            }
        };
        var statsResult = await stats.GetSchools("2026", "6to A", CancellationToken.None);
        var rows = Assert.IsType<OkObjectResult>(statsResult).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rows));
        Assert.Contains(json.RootElement.EnumerateArray(), row => row.GetProperty("cue").GetString() == "180000100");
    }

    [Fact]
    public async Task Admin_reprocess_grades_a_previously_unknown_version_and_rebuild_is_idempotent()
    {
        using var dbContext = CreateDbContext();
        const string attemptId = "recoverable-attempt";
        const string versionId = "recoverable-version";
        const string blockId = "recoverable-block";
        dbContext.Schools.Add(new School("school-recovery", "CUE-180000100", 180000100, null, "Escuela", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();
        var item = CreateAttemptItem("recoverable-idem", attemptId, versionId,
            new[] { MakeAnswerPayload(attemptId, blockId, "[\"B\"]") },
            new { id = "recoverable-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", examVersionId = versionId, status = "closed" });

        var firstPush = await PushAsync(dbContext, item);
        Assert.Equal("accepted", firstPush.Results.Single().Status);
        var received = await dbContext.ReceivedStudentAttempts.SingleAsync();
        Assert.Equal("ungradable", (await dbContext.CentralAttemptResults.SingleAsync()).Status);

        var exam = MakeExam("recoverable-exam");
        var version = MakeVersion(versionId, exam.Id);
        var block = MakeBlock(blockId, version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("recoverable-key", block.Id, "[\"B\"]", 1m));
        await dbContext.SaveChangesAsync();

        var rollups = new CentralStatsRollupService(dbContext);
        var admin = new ReceivedSyncAdminController(dbContext, rollups, NullLogger<ReceivedSyncAdminController>.Instance);
        var firstReprocess = Assert.IsType<OkObjectResult>(await admin.Reprocess(CancellationToken.None));
        Assert.Equal("graded", (await dbContext.CentralAttemptResults.SingleAsync()).Status);
        var firstRollup = await dbContext.ExamRollups.SingleAsync();
        Assert.Equal(1, firstRollup.AttemptCount);
        Assert.Equal("attributed", (await dbContext.ReceivedStudentAttempts.SingleAsync()).AttributionStatus);

        var secondReprocess = Assert.IsType<OkObjectResult>(await admin.Reprocess(CancellationToken.None));
        Assert.Equal(1, (await dbContext.ExamRollups.SingleAsync()).AttemptCount);
        Assert.Equal(received.Id, (await dbContext.ReceivedStudentAttempts.SingleAsync()).Id);
        Assert.NotNull(firstReprocess.Value);
        Assert.NotNull(secondReprocess.Value);
    }

    [Fact]
    public async Task Reprocess_persists_unattributed_status_and_reason_for_ungradable_attempts()
    {
        using var dbContext = CreateDbContext();
        dbContext.Schools.Add(new School("school-unattributed", "CUE-180000100", 180000100, null, "Escuela", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();
        var item = CreateAttemptItem("unattributed-idem", "unattributed-attempt", "missing-version",
            new[] { MakeAnswerPayload("unattributed-attempt", "missing-block", "[\"B\"]") },
            new { id = "unattributed-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", examVersionId = "missing-version", status = "closed" });
        Assert.Equal("accepted", (await PushAsync(dbContext, item)).Results.Single().Status);

        var admin = new ReceivedSyncAdminController(dbContext, new CentralStatsRollupService(dbContext), NullLogger<ReceivedSyncAdminController>.Instance);
        Assert.IsType<OkObjectResult>(await admin.Reprocess(CancellationToken.None));

        var attempt = await dbContext.ReceivedStudentAttempts.AsNoTracking().SingleAsync();
        Assert.Equal("unattributed", attempt.AttributionStatus);
        Assert.False(string.IsNullOrWhiteSpace(attempt.AttributionReason));
    }

    [Fact]
    public async Task Reprocess_regrades_every_page_of_attempts_and_stays_idempotent()
    {
        using var dbContext = CreateDbContext();
        const string versionId = "paged-version";
        const string blockId = "paged-block";
        dbContext.Schools.Add(new School("school-paged", "CUE-180000100", 180000100, null, "Escuela", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();
        for (var index = 0; index < 3; index++)
        {
            var attemptId = $"paged-attempt-{index}";
            var response = await PushAsync(dbContext, CreateAttemptItem($"paged-idem-{index}", attemptId, versionId,
                new[] { MakeAnswerPayload(attemptId, blockId, "[\"B\"]") },
                new { id = "paged-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", examVersionId = versionId, status = "closed" }));
            Assert.Equal("accepted", response.Results.Single().Status);
        }
        Assert.All(await dbContext.CentralAttemptResults.ToListAsync(), result => Assert.Equal("ungradable", result.Status));

        var exam = MakeExam("paged-exam");
        var version = MakeVersion(versionId, exam.Id);
        var block = MakeBlock(blockId, version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("paged-key", block.Id, "[\"B\"]", 1m));
        await dbContext.SaveChangesAsync();

        var admin = new ReceivedSyncAdminController(dbContext, new CentralStatsRollupService(dbContext), NullLogger<ReceivedSyncAdminController>.Instance)
        {
            RegradePageSize = 2
        };
        Assert.IsType<OkObjectResult>(await admin.Reprocess(CancellationToken.None));
        Assert.IsType<OkObjectResult>(await admin.Reprocess(CancellationToken.None));

        Assert.All(await dbContext.CentralAttemptResults.AsNoTracking().ToListAsync(), result => Assert.Equal("graded", result.Status));
        Assert.All(await dbContext.ReceivedStudentAttempts.AsNoTracking().ToListAsync(), attempt => Assert.Equal("attributed", attempt.AttributionStatus));
        Assert.Equal(3, (await dbContext.ExamRollups.AsNoTracking().SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Duplicate_push_of_a_graded_attempt_still_returns_duplicate()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("graded-duplicate-exam");
        var version = MakeVersion("graded-duplicate-version", exam.Id);
        var block = MakeBlock("graded-duplicate-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("graded-duplicate-key", block.Id, "[\"B\"]", 1m));
        await dbContext.SaveChangesAsync();
        var item = CreateAttemptItem("graded-duplicate-idem", "graded-duplicate-attempt", version.Id,
            new[] { MakeAnswerPayload("graded-duplicate-attempt", block.Id, "[\"B\"]") });

        Assert.Equal("accepted", (await PushAsync(dbContext, item)).Results.Single().Status);
        Assert.Equal("duplicate", (await PushAsync(dbContext, item)).Results.Single().Status);
        Assert.Equal("graded", (await dbContext.CentralAttemptResults.SingleAsync()).Status);
    }

    [Fact]
    public async Task Storage_failure_while_updating_the_rollup_is_not_swallowed_as_ungradable()
    {
        var failOnSave = new FailOnSaveInterceptor();
        using var dbContext = CreateDbContext(failOnSave);
        var exam = MakeExam("storage-failure-exam");
        var version = MakeVersion("storage-failure-version", exam.Id);
        var block = MakeBlock("storage-failure-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("storage-failure-key", block.Id, "[\"B\"]", 1m));
        dbContext.Schools.Add(new School("school-storage", "CUE-180000100", 180000100, null, "Escuela", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();
        await PushAsync(dbContext, CreateAttemptItem("storage-failure-idem", "storage-failure-attempt", version.Id,
            new[] { MakeAnswerPayload("storage-failure-attempt", block.Id, "[\"B\"]") },
            new { id = "storage-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", examVersionId = version.Id, status = "closed" }));
        var received = await dbContext.ReceivedStudentAttempts.SingleAsync();
        var grader = new CentralAttemptGradingService(dbContext, new CentralStatsRollupService(dbContext));

        failOnSave.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => grader.RecomputeAsync(received.Id, version.Id,
            [new ReceivedSubmissionAnswer("a", received.RemoteLocalId, block.Id, JsonDocument.Parse("[\"B\"]"), Now)],
            CancellationToken.None));

        Assert.DoesNotContain(dbContext.CentralAttemptResults.Local, result => result.Status == "ungradable");
    }

    [Fact]
    public async Task Durable_receipt_survives_processing_fault_and_same_key_retries_processing()
    {
        var fault = new FailFirstRollupSaveInterceptor();
        using var dbContext = CreateDbContext(fault);
        var exam = MakeExam("durable-receipt-exam");
        var version = MakeVersion("durable-receipt-version", exam.Id);
        var block = MakeBlock("durable-receipt-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("durable-receipt-key", block.Id, "[\"B\"]", 1m));
        dbContext.Schools.Add(new School("durable-receipt-school", "CUE-180000100", 180000100, null, "Escuela", "locality-1", "Active", null, Now, Now));
        await dbContext.SaveChangesAsync();

        fault.Armed = true;
        var item = CreateAttemptItem("durable-receipt-idem", "durable-receipt-attempt", version.Id,
            new[] { MakeAnswerPayload("durable-receipt-attempt", block.Id, "[\"B\"]") },
            new { id = "durable-receipt-session", schoolCue = "180000100", schoolYear = "2026", course = "6to A", examVersionId = version.Id, status = "closed" });
        var first = await PushAsync(dbContext, item);

        Assert.Equal("accepted", first.Results.Single().Status);
        Assert.Equal("processing_failed", first.Results.Single().ProcessingStatus);
        Assert.NotNull(first.Results.Single().ReceivedAt);
        Assert.Equal("processing_failed", (await dbContext.SyncInbox.SingleAsync()).Status);
        Assert.Equal(1, (await dbContext.SyncInbox.SingleAsync()).ProcessingAttemptCount);
        var failedInbox = await dbContext.SyncInbox.SingleAsync();
        Assert.True(failedInbox.NextProcessingAt.HasValue && failedInbox.NextProcessingAt.Value > DateTimeOffset.UtcNow);
        Assert.Empty(await dbContext.ReceivedStudentAttempts.ToListAsync());

        var retry = await PushAsync(dbContext, item);

        Assert.Equal("duplicate", retry.Results.Single().Status);
        Assert.Equal("processed", retry.Results.Single().ProcessingStatus);
        Assert.Equal(first.Results.Single().ReceivedAt, retry.Results.Single().ReceivedAt);
        Assert.Equal(1, await dbContext.SyncInbox.CountAsync());
        Assert.Null((await dbContext.SyncInbox.SingleAsync()).NextProcessingAt);
        Assert.Equal(1, await dbContext.ReceivedStudentAttempts.CountAsync());
        Assert.Equal(1, await dbContext.CentralAttemptResults.CountAsync());
        Assert.Equal(1, (await dbContext.ExamRollups.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Push_batch_isolates_a_bad_attempt_and_accepts_the_following_item()
    {
        using var dbContext = CreateDbContext();
        var invalidPayload = JsonSerializer.SerializeToElement(new { answers = Array.Empty<object>() });
        var invalid = new PushItem("bad-item-key", SyncEventTypes.AttemptSubmitted, "student_attempt", "bad-attempt",
            invalidPayload, SyncPayloadChecksum.Calculate(invalidPayload), Now.ToString("O"));
        var valid = CreateAttemptItem("good-item-key", "good-attempt", null, Array.Empty<object>());
        var controller = new SyncController(dbContext, new CentralStatsRollupService(dbContext));
        SyncTestPrincipals.BindNode(controller, "node-1");

        var result = await controller.Push(new PushRequest("node-1", [invalid, valid]), "node-1", new PushRequestValidator(), CancellationToken.None);
        var response = Assert.IsType<PushResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(new[] { "failed", "accepted" }, response.Results.Select(item => item.Status));
        Assert.Equal(1, response.Received);
        Assert.Equal(1, response.Failed);
        Assert.Single(await dbContext.ReceivedStudentAttempts.ToListAsync());
    }

    [Fact]
    public async Task Unknown_session_cue_persists_attempt_attribution_reason()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("unknown-cue-exam");
        var version = MakeVersion("unknown-cue-version", exam.Id);
        var block = MakeBlock("unknown-cue-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("unknown-cue-answer-key", block.Id, "[\"B\"]", 1m));
        await dbContext.SaveChangesAsync();
        var item = CreateAttemptItem("unknown-cue-idem", "unknown-cue-attempt", version.Id,
            new[] { MakeAnswerPayload("unknown-cue-attempt", block.Id, "[\"B\"]") },
            new { id = "unknown-cue-session", schoolCue = "bad-cue", schoolYear = "2026", course = "6to A", examVersionId = version.Id });

        var response = await PushAsync(dbContext, item);

        Assert.Equal("accepted", response.Results.Single().Status);
        Assert.Empty(await dbContext.ExamRollups.ToListAsync());
        var attempt = await dbContext.ReceivedStudentAttempts.SingleAsync();
        Assert.Equal("unattributed", attempt.AttributionStatus);
        Assert.Contains("known CUE", attempt.AttributionReason);
    }

    [Fact]
    public async Task Unexpected_grading_exception_is_stored_as_ungradable_with_reason()
    {
        using var dbContext = CreateDbContext();
        var exam = MakeExam("grading-exception-exam");
        var version = MakeVersion("grading-exception-version", exam.Id);
        var block = MakeBlock("grading-exception-block", version.Id);
        SeedExam(dbContext, exam, version, block, MakeAnswerKey("grading-exception-key", block.Id, "[1]", 1m));
        await dbContext.SaveChangesAsync();

        var response = await PushAsync(dbContext, CreateAttemptItem("grading-exception-idem", "grading-exception-attempt", version.Id,
            new[] { MakeAnswerPayload("grading-exception-attempt", block.Id, "[\"B\"]") }));

        Assert.Equal("accepted", response.Results.Single().Status);
        var result = await dbContext.CentralAttemptResults.SingleAsync();
        Assert.Equal("ungradable", result.Status);
        Assert.Contains("Grading failed", result.Reason);
        Assert.Contains("string", result.Reason, StringComparison.OrdinalIgnoreCase);
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
        var result = await dbContext.CentralAttemptResults.SingleAsync();
        Assert.Equal("ungradable", result.Status);
        Assert.Contains("did not include an exam version", result.Reason);
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

    private static IServiceScope CreateAuthorizationScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => options.AddPolicy("RosterCueAccess", policy => policy.Requirements.Add(new RosterScopeRequirement())));
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
    }

    private static async Task AssertStatsContainsCueAsync(PlanCopeDbContext dbContext, string cue, string schoolYear, string course)
    {
        using var scope = CreateAuthorizationScope();
        var controller = new StatsController(dbContext, scope.ServiceProvider.GetRequiredService<IAuthorizationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roster_scope", "province")], "test"))
                }
            }
        };
        var result = await controller.GetSchools(schoolYear, course, CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value));
        Assert.Contains(json.RootElement.EnumerateArray(), row => row.GetProperty("cue").GetString() == cue);
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

    private static PlanCopeDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .AddInterceptors(interceptors)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PlanCopeDbContext(options);
    }

    private sealed class FailOnSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed ? throw new DbUpdateException("Simulated storage failure.") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class FailFirstRollupSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context?.ChangeTracker.Entries<ExamRollup>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                Armed = false;
                throw new DbUpdateException("Synthetic rollup persistence failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

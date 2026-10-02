using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Grading;
using PlanCope.Shared.Infrastructure.Validation;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class CentralStatsPostgresIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string NodeId = "integration-node";
    private const string ExamVersionId = "postgres-ev-1";
    private const string BlockId = "postgres-block-1";
    private const string SectionId = "postgres-section-1";

    [DockerFact]
    public async Task Failed_rollup_commit_leaves_no_partial_receipt_and_same_key_retry_converges_once()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var baseOptions = CreateOptions(connectionString);
        await using (var migrationContext = new PlanCopeDbContext(baseOptions))
        {
            await migrationContext.Database.MigrateAsync();
            await SeedAsync(migrationContext);
        }

        var item = CreateItem("durability-key", "durability-attempt", "durability-session");
        var faultedOptions = CreateOptions(connectionString, new FailFirstRollupSaveInterceptor());
        var receipt = await PushAsync(faultedOptions, item);
        var receiptResult = Assert.Single(receipt.Results);
        Assert.Equal("accepted", receiptResult.Status);
        Assert.Equal("processing_failed", receiptResult.ProcessingStatus);
        Assert.NotNull(receiptResult.ReceivedAt);

        await using (var afterFault = new PlanCopeDbContext(baseOptions))
        {
            Assert.Equal("processing_failed", (await afterFault.SyncInbox.SingleAsync()).Status);
            Assert.Empty(await afterFault.ReceivedStudentAttempts.ToListAsync());
            Assert.Empty(await afterFault.CentralAttemptResults.ToListAsync());
            Assert.Empty(await afterFault.ExamRollups.ToListAsync());
        }

        var retry = await PushAsync(baseOptions, item);
        var duplicate = await PushAsync(baseOptions, item);
        var conflictPayload = JsonSerializer.SerializeToElement(new { attempt = new { id = "different-content", studentCode = "STUDENT-1" } });
        var conflict = await PushAsync(baseOptions, item with { Payload = conflictPayload, Checksum = SyncPayloadChecksum.Calculate(conflictPayload) });

        Assert.Equal("duplicate", Assert.Single(retry.Results).Status);
        Assert.Equal("processed", Assert.Single(retry.Results).ProcessingStatus);
        Assert.Equal("duplicate", Assert.Single(duplicate.Results).Status);
        Assert.Equal(receiptResult.ReceivedAt, Assert.Single(retry.Results).ReceivedAt);
        Assert.Equal(receiptResult.ReceivedAt, Assert.Single(duplicate.Results).ReceivedAt);
        Assert.Equal("failed", Assert.Single(conflict.Results).Status);
        await using var final = new PlanCopeDbContext(baseOptions);
        Assert.Equal(1, await final.SyncInbox.CountAsync(row => row.IdempotencyKey == "durability-key"));
        Assert.Equal(1, await final.ReceivedStudentAttempts.CountAsync(row => row.RemoteLocalId == "durability-attempt"));
        Assert.Equal(1, await final.CentralAttemptResults.CountAsync());
        Assert.Equal(1, (await final.ExamRollups.SingleAsync()).AttemptCount);
    }

    [DockerFact]
    public async Task Postgres_rollups_serialize_rebuilds_and_concurrent_session_pushes()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = CreateOptions(connectionString);
        await using (var migrationContext = new PlanCopeDbContext(options))
        {
            await migrationContext.Database.MigrateAsync();
            await SeedAsync(migrationContext);
        }

        await PushAsync(options, CreateItem("baseline-key", "baseline-attempt", "baseline-session"));

        await using (var rebuildContext = new PlanCopeDbContext(options))
        {
            var rebuilt = await new CentralStatsRollupService(rebuildContext, NullLogger<CentralStatsRollupService>.Instance).RebuildAllAsync();
            Assert.Equal(1, rebuilt);
            var rollup = await rebuildContext.ExamRollups.AsNoTracking().SingleAsync();
            Assert.Equal(1, rollup.AttemptCount);
            Assert.Equal(1d, rollup.ScoreSum);
        }

        await InstallSlowRebuildTriggerAsync(connectionString);
        var rebuildTask = Task.Run(async () =>
        {
            await using var context = new PlanCopeDbContext(options);
            return await new CentralStatsRollupService(context, NullLogger<CentralStatsRollupService>.Instance).RebuildAllAsync();
        });
        await WaitForAdvisoryLockAsync(connectionString);

        await using (var concurrentContext = new PlanCopeDbContext(options))
        {
            var controller = new StatsRollupAdminController(
                new CentralStatsRollupService(concurrentContext, NullLogger<CentralStatsRollupService>.Instance));
            Assert.IsType<ConflictObjectResult>((await controller.Rebuild(CancellationToken.None)).Result);
        }

        var pushDuringRebuild = PushAsync(options, CreateItem("during-rebuild-key", "during-rebuild-attempt", "baseline-session"));
        await Task.Delay(100);
        Assert.False(pushDuringRebuild.IsCompleted);
        Assert.Equal(1, await rebuildTask);
        Assert.Equal("accepted", (await pushDuringRebuild).Results.Single().Status);

        await RemoveSlowRebuildTriggerAsync(connectionString);
        await using (var checkContext = new PlanCopeDbContext(options))
        {
            var rollup = await checkContext.ExamRollups.AsNoTracking().SingleAsync();
            Assert.Equal(2, rollup.AttemptCount);
            var rebuilt = await new CentralStatsRollupService(checkContext, NullLogger<CentralStatsRollupService>.Instance).RebuildAllAsync();
            Assert.Equal(2, rebuilt);
            Assert.Equal(2, (await checkContext.ExamRollups.AsNoTracking().SingleAsync()).AttemptCount);
        }

        await AssertConcurrentFirstSessionPushesRecoverFromUniqueConflictAsync(connectionString);
        await using var finalContext = new PlanCopeDbContext(options);
        Assert.Equal(1, await finalContext.DeliverySessions.CountAsync(session => session.SourceNodeId == NodeId && session.RemoteLocalId == "racing-session"));
        Assert.Equal(4, (await finalContext.ExamRollups.AsNoTracking().SingleAsync()).AttemptCount);
    }

    private static async Task AssertConcurrentFirstSessionPushesRecoverFromUniqueConflictAsync(string connectionString)
    {
        var interceptor = new PauseOnSessionInsertInterceptor();
        var racingOptions = CreateOptions(connectionString, interceptor);
        var options = CreateOptions(connectionString);
        var first = PushAsync(racingOptions, CreateItem("racing-key-1", "racing-attempt-1", "racing-session"));
        await interceptor.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = PushAsync(options, CreateItem("racing-key-2", "racing-attempt-2", "racing-session"));
        await Task.Delay(100);

        await InsertCompetingSessionAsync(connectionString, "racing-session");
        interceptor.Release.TrySetResult(true);

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, response => Assert.Equal("accepted", response.Results.Single().Status));
    }

    private static async Task SeedAsync(PlanCopeDbContext dbContext)
    {
        dbContext.Exams.Add(new Exam("postgres-exam-1", "PG-EXAM-1", "PostgreSQL Integration Exam", null,
            ["primaria-6"], "Matematica", "Numeros", "Approved", null, Now, Now));
        dbContext.ExamVersions.Add(new PlanCope.Shared.Domain.Central.ExamVersion(ExamVersionId, "postgres-exam-1", 1, 1, "Approved", null,
            null, null, null, null, null, Now, Now));
        dbContext.ExamBlocks.Add(new ExamBlock(BlockId, ExamVersionId, 0, BlockType.MultipleChoice, "Question",
            "Choose B", JsonDocument.Parse("""{"options":["A","B"]}"""), null, Now, Now));
        dbContext.AnswerKeys.Add(new AnswerKey("postgres-answer-key-1", BlockId,
            JsonDocument.Parse("""["B"]"""), 1m, null, Now, Now));
        dbContext.GeRosterSnapshots.Add(new GeRosterSnapshot
        {
            Id = "postgres-snapshot-1", Cue = "180000100", SchoolYear = "2026", FetchedAt = Now,
            Checksum = "postgres-test", SectionCount = 1, StudentCount = 1, Status = "current"
        });
        dbContext.GeRosterSections.Add(new GeRosterSection
        {
            Id = SectionId, SnapshotId = "postgres-snapshot-1", Course = "6to A"
        });
        await dbContext.SaveChangesAsync();
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions(string connectionString, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly(typeof(PlanCope.Central.Migrations.Migrations.AddDeliverySessionNodeOwnership).Assembly.GetName().Name));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    private sealed class FailFirstRollupSaveInterceptor : SaveChangesInterceptor
    {
        private int armed = 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<ExamRollup>().Any(entry => entry.State == EntityState.Added) == true &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Synthetic rollup write fault after inbox/attempt staging.");

            return ValueTask.FromResult(result);
        }
    }

    private static async Task<PushResponse> PushAsync(DbContextOptions<PlanCopeDbContext> options, PushItem item)
    {
        await using var dbContext = new PlanCopeDbContext(options);
        var controller = new SyncController(dbContext, new CentralStatsRollupService(dbContext, NullLogger<CentralStatsRollupService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("token_type", "node_access"), new Claim("node_id", NodeId) }, "integration-test"))
            }
        };
        var response = await controller.Push(new PushRequest(NodeId, new[] { item }), NodeId,
            new PushRequestValidator(), CancellationToken.None);
        return Assert.IsType<PushResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);
    }

    private static PushItem CreateItem(string idempotencyKey, string attemptId, string sessionId)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            attempt = new
            {
                id = attemptId,
                deliverySessionId = sessionId,
                studentCode = "STUDENT-1",
                status = "submitted",
                startedAt = Now.ToString("O"),
                submittedAt = Now.AddMinutes(2).ToString("O"),
                localSequence = 1,
                confirmationCode = "PGTEST"
            },
            answers = new[]
            {
                new { id = $"answer-{attemptId}", studentAttemptId = attemptId, blockId = BlockId, answerJson = "[\"B\"]", createdAt = Now.AddMinutes(1).ToString("O") }
            },
            rosterSnapshotId = "postgres-snapshot-1",
            rosterSectionId = SectionId,
            examVersionRemoteId = ExamVersionId,
            deliverySession = new
            {
                id = sessionId,
                schoolCue = "180000100",
                schoolYear = "2026",
                course = "6to A",
                sectionId = SectionId,
                examVersionId = ExamVersionId,
                startedAt = Now.ToString("O"),
                closedAt = Now.AddMinutes(3).ToString("O"),
                status = "closed"
            }
        });
        return new PushItem(idempotencyKey, SyncEventTypes.AttemptSubmitted, "student_attempt", attemptId,
            payload, SyncPayloadChecksum.Calculate(payload), Now.AddMinutes(2).ToString("O"));
    }

    private static async Task InstallSlowRebuildTriggerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE FUNCTION stats.integration_pause_rollup_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_sleep(1.5); RETURN NULL; END $$;
            CREATE TRIGGER integration_pause_rollup_delete BEFORE DELETE ON stats.exam_rollups
            FOR EACH STATEMENT EXECUTE FUNCTION stats.integration_pause_rollup_delete();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSlowRebuildTriggerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER integration_pause_rollup_delete ON stats.exam_rollups; DROP FUNCTION stats.integration_pause_rollup_delete();";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitForAdvisoryLockAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND classid = 1284731029::oid AND objid = 1::oid AND granted);";
            if ((bool)(await command.ExecuteScalarAsync() ?? false)) return;
            await Task.Delay(25);
        }

        throw new TimeoutException("The statistics rebuild did not acquire its PostgreSQL advisory lock.");
    }

    private static async Task InsertCompetingSessionAsync(string connectionString, string localSessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync.delivery_sessions
                ("Id", "RemoteLocalId", "SchoolId", "ExamVersionId", "ClassroomCode", "CommissionCode", "Status", "StartedAt", "EndedAt", "SyncedAt", "CreatedAt", "SourceNodeId")
            VALUES (@id, @remoteId, '180000100', @examVersionId, NULL, NULL, 'closed', @startedAt, @endedAt, @syncedAt, @createdAt, @nodeId);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("remoteId", localSessionId);
        command.Parameters.AddWithValue("examVersionId", ExamVersionId);
        command.Parameters.AddWithValue("startedAt", Now);
        command.Parameters.AddWithValue("endedAt", Now.AddMinutes(3));
        command.Parameters.AddWithValue("syncedAt", Now);
        command.Parameters.AddWithValue("createdAt", Now);
        command.Parameters.AddWithValue("nodeId", NodeId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class PauseOnSessionInsertInterceptor : SaveChangesInterceptor
    {
        private int armed = 1;
        public TaskCompletionSource<bool> Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker
                    .Entries<CentralDeliverySession>().Any(entry => entry.State == EntityState.Added) == true &&
                Interlocked.Exchange(ref armed, 0) == 1)
            {
                Paused.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}

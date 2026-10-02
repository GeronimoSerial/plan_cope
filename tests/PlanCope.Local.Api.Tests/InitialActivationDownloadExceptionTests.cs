using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class InitialActivationDownloadExceptionTests
{
    [Theory]
    [InlineData("push")]
    [InlineData("exam")]
    [InlineData("roster")]
    public async Task Any_download_stage_exception_leaves_activation_pending_for_retry(string stage)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-download-{Guid.NewGuid():N}.db");
        try
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            var options = new LocalDatabaseOptions(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
            new LocalDatabaseInitializer(options).Initialize();
            var connections = new LocalSqliteConnectionFactory(options);
            var state = new SyncStateRepository(connections);
            var outbox = new OutboxRepository(connections);
            var exams = new LocalExamRepository(connections);
            var identity = new NodeIdentityRepository(connections);
            var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
            var refresher = new NodeCredentialRefresher(services.GetRequiredService<IHttpClientFactory>(), state, identity);
            var assets = new LocalAssetFileService(new ConfigurationBuilder().AddInMemoryCollection().Build(), exams);
            var submission = new AttemptSubmissionService(new AttemptRepository(connections), new SessionRepository(connections),
                exams, new StatsRollupRepository(connections, NullLogger<StatsRollupRepository>.Instance), connections,
                NullLogger<AttemptSubmissionService>.Instance);
            var revalidation = new ActivationRevalidationService(identity, state, connections, refresher, submission, assets,
                NullLogger<ActivationRevalidationService>.Instance, TimeProvider.System);
            await revalidation.SetActivationInProgressAsync(true);

            if (stage == "push")
                await outbox.InsertAsync(new SyncOutbox("pending", "attempt_submitted", "student_attempt", "a", "key", "{}",
                    "pending", 0, null, null, DateTimeOffset.UtcNow.ToString("O"), null));
            var download = new InitialActivationDownloadService(outbox,
                state,
                new ThrowingPushService(), new StubExamPullService(stage == "exam"),
                new StubRosterPullService(stage == "roster"), revalidation, NullLogger<InitialActivationDownloadService>.Instance);

            var result = await download.DownloadAllAsync();

            Assert.False(result.Success);
            Assert.Contains("Reintentá la descarga", result.Error);
            Assert.True(await revalidation.IsActivationInProgressAsync());
            var progressState = await state.GetAsync("activation_download_progress");
            Assert.NotNull(progressState);
            var progress = JsonSerializer.Deserialize<ActivationDownloadProgress>(progressState.ValueJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(stage == "push" ? "pending-results" : "exams", progress?.Phase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private sealed class ThrowingPushService : ILocalOutboxPushService
    {
        public Task<LocalOutboxPushResult> PushAsync(int requestedLimit, CancellationToken cancellationToken = default) =>
            throw new IOException("push failed");
    }

    private sealed class StubExamPullService(bool shouldThrow) : ILocalExamPullService
    {
        public Task<LocalExamPullResult> PullAsync(CancellationToken cancellationToken = default) => shouldThrow
            ? throw new IOException("exam pull failed")
            : Task.FromResult(new LocalExamPullResult(true, 0, "0", null));
    }

    private sealed class StubRosterPullService(bool shouldThrow) : ILocalRosterPullService
    {
        public Task<LocalRosterBulkPullResult> PullAllAsync(CancellationToken cancellationToken = default) => shouldThrow
            ? throw new IOException("roster pull failed")
            : Task.FromResult(new LocalRosterBulkPullResult(true, 0, 0, 0, null));
    }
}

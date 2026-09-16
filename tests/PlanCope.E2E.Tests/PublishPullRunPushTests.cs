using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Local.Api;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.Local;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.E2E.Tests;

public sealed class PublishPullRunPushTests
{
    [Fact]
    public async Task Exam_published_on_central_is_pulled_run_and_pushed_back_to_central()
    {
        using var centralFactory = new CentralApiFactory();
        var accessToken = CreateAccessToken();

        using var centralClient = centralFactory.CreateClient();
        centralClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var exam = await CreateExamOnCentralAsync(centralClient);
        var version = await CreateExamVersionOnCentralAsync(centralClient, exam.Id);
        var block = await AddBlockOnCentralAsync(centralClient, version.Id);
        await PublishVersionOnCentralAsync(centralClient, version.Id);

        using var localFactory = new LocalApiFactory(centralFactory);
        using var localClient = localFactory.CreateClient();
        var health = await localClient.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await SeedSyncStateAsync(localFactory, centralFactory.PlaceholderCentralUrl, accessToken);

        using (var pullScope = localFactory.Services.CreateScope())
        {
            var pull = pullScope.ServiceProvider.GetRequiredService<LocalExamPullService>();
            var result = await pull.PullAsync(CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Imported >= 1, $"expected at least one imported exam version but got {result.Imported}");
        }

        var attemptId = await RunSessionAndSubmitAsync(localClient, version.Id, block.Id);

        using (var pushScope = localFactory.Services.CreateScope())
        {
            var push = pushScope.ServiceProvider.GetRequiredService<LocalOutboxPushService>();
            var result = await push.PushAsync(50, CancellationToken.None);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Accepted >= 1, $"expected at least one accepted outbox item but got {result.Accepted}");
        }

        using (var centralScope = centralFactory.Services.CreateScope())
        {
            var db = centralScope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            var received = await db.ReceivedStudentAttempts
                .SingleOrDefaultAsync(x => x.RemoteLocalId == attemptId);
            Assert.NotNull(received);
            Assert.Equal("submitted", received.Status);
        }
    }

    private static string CreateAccessToken()
    {
        var tokenService = new TokenService(Options.Create(new AuthOptions { SigningKey = CentralApiFactory.SigningKey }));
        return tokenService.CreateAccessToken(new UserProfileDto("e2e-user-id", "E2E User", "Admin", null, "province", []));
    }

    private static async Task<ExamCreated> CreateExamOnCentralAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/exams", new CreateExamRequest(
            "E2E-MAT-001", "Matematica E2E", null, "Primaria", "Matematica", "Matematica"));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var exam = await response.Content.ReadFromJsonAsync<ExamCreated>();
        Assert.NotNull(exam);
        Assert.False(string.IsNullOrWhiteSpace(exam!.Id));
        return exam;
    }

    private static async Task<VersionCreated> CreateExamVersionOnCentralAsync(HttpClient client, string examId)
    {
        var response = await client.PostAsJsonAsync($"/api/exams/{examId}/versions", new CreateExamVersionRequest(
            1, JsonSerializer.Deserialize<JsonElement>("{}"), "ProportionalPenalised"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<VersionCreated>();
        Assert.NotNull(version);
        Assert.False(string.IsNullOrWhiteSpace(version!.Id));
        return version;
    }

    private static async Task<BlockCreated> AddBlockOnCentralAsync(HttpClient client, string versionId)
    {
        var config = JsonSerializer.Deserialize<JsonElement>("""
            {"question":"Cuanto es 18 + 24?","options":[{"value":"42","label":"42"},{"value":"44","label":"44"}]}
            """);
        var response = await client.PutAsJsonAsync($"/api/exams/versions/{versionId}/blocks", new UpsertBlockRequest(
            0, BlockType.MultipleChoice, "Pregunta 1", null, config, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var block = await response.Content.ReadFromJsonAsync<BlockCreated>();
        Assert.NotNull(block);
        Assert.False(string.IsNullOrWhiteSpace(block!.Id));
        return block;
    }

    private static async Task PublishVersionOnCentralAsync(HttpClient client, string versionId)
    {
        var response = await client.PostAsJsonAsync($"/api/exams/versions/{versionId}/publish", new PublishExamVersionRequest(
            "Matematica", "6", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var publish = await response.Content.ReadFromJsonAsync<PublishExamVersionResponse>();
        Assert.NotNull(publish);
        Assert.False(string.IsNullOrWhiteSpace(publish!.Checksum));
    }

    private static async Task SeedSyncStateAsync(LocalApiFactory factory, string centralUrl, string accessToken)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISyncStateRepository>();
        var now = DateTimeOffset.UtcNow.ToString("O");
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "central_url", JsonSerializer.Serialize(centralUrl), now));
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "node_id", JsonSerializer.Serialize("e2e-node-1"), now));
        await repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), "central_access_token", JsonSerializer.Serialize(accessToken), now));
    }

    private static async Task<string> RunSessionAndSubmitAsync(HttpClient client, string examVersionId, string blockId)
    {
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            examVersionId, "180055400", "6A", null, "Operador", 30, null));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session!.AccessCode));

        var startResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        Assert.NotNull(started!.Attempt);
        Assert.NotEmpty(started.Blocks);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            answers = new[]
            {
                new { blockId, answer = "42" }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var confirmation = await submitResponse.Content.ReadFromJsonAsync<SubmitAttemptResponse>();
        Assert.NotNull(confirmation);
        Assert.False(string.IsNullOrWhiteSpace(confirmation!.ConfirmationCode));

        return started.Attempt.Id;
    }

    private sealed record ExamCreated(string Id);

    private sealed record VersionCreated(string Id);

    private sealed record BlockCreated(string Id);

    private sealed record StartAttemptResponse(StudentAttempt Attempt, IReadOnlyList<LocalExamBlock> Blocks);

    private sealed class CentralApiFactory : WebApplicationFactory<PlanCopeDbContext>
    {
        private static readonly string DbName = Guid.NewGuid().ToString("N");

        public const string SigningKey = "e2e-test-signing-key-with-at-least-64-characters-0123456789abcdef0123456789abcdef";

        public CentralApiFactory()
        {
            // builder.Configuration.GetSection("Auth") in Program.cs runs before
            // WebApplication.Build(), so ConfigureAppConfiguration below never reaches it in
            // time (same minimal-hosting-model ordering gotcha LocalApiFactory works around
            // with an environment variable for its connection string). An env var set before
            // the host starts is read by builder.Configuration like any other source.
            Environment.SetEnvironmentVariable("Auth__SigningKey", SigningKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:SigningKey"] = SigningKey
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<PlanCopeDbContext>>();
                services.AddDbContext<PlanCopeDbContext>(options =>
                    options.UseInMemoryDatabase(DbName)
                        .ReplaceService<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
                        // SyncController opens a real transaction, which the InMemory provider
                        // doesn't support - it only warns (and EF is configured to throw on
                        // warnings), it doesn't actually need one to behave correctly here.
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        // Local's sync services (LocalExamPullService/LocalOutboxPushService) build their own
        // HttpClient and set its BaseAddress from a "central_url" string read out of
        // sync_state at runtime - they are not aware this is a test. Rather than making
        // WebApplicationFactory bind a real Kestrel socket (fragile: WebApplicationFactory's
        // CreateHost never actually starts a real listener, it only wires TestServer's
        // in-memory transport, so a real HttpClient pointed at the reported address gets
        // "connection refused"), those two named clients get their PRIMARY HANDLER replaced
        // with TestServer's own in-memory handler in LocalApiFactory below. BaseAddress then
        // only needs to be a syntactically valid absolute URI - TestServer's handler dispatches
        // by request path, not by host/port - so this placeholder is fine to hand out as
        // "central_url".
        public string PlaceholderCentralUrl => Server.BaseAddress.ToString();

        public HttpMessageHandler CreateInProcessHandler() => Server.CreateHandler();
    }

    private sealed class LocalApiFactory : WebApplicationFactory<LocalDatabaseInitializer>
    {
        private readonly CentralApiFactory centralFactory;
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-e2e-local-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public LocalApiFactory(CentralApiFactory centralFactory)
        {
            this.centralFactory = centralFactory;
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            previousSeedDemoExam = Environment.GetEnvironmentVariable("Local__SeedDemoExam");
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false",
                    ["Nominalization:DocumentHmacKey"] = "e2e-test-hmac-key-with-at-least-32-bytes"
                });
            });
            builder.ConfigureServices(services =>
            {
                // Route Local's two Central-bound named HttpClients through Central's own
                // in-memory TestServer pipeline instead of a real socket - see the comment on
                // CentralApiFactory.CreateInProcessHandler for why.
                services.AddHttpClient(nameof(LocalExamPullService))
                    .ConfigurePrimaryHttpMessageHandler(centralFactory.CreateInProcessHandler);
                services.AddHttpClient(nameof(LocalOutboxPushService))
                    .ConfigurePrimaryHttpMessageHandler(centralFactory.CreateInProcessHandler);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", previousConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", previousSeedDemoExam);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

}
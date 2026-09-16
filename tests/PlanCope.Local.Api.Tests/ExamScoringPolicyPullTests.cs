using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class ExamScoringPolicyPullTests : IDisposable
{
    private const string ScoringPolicyValue = "ProportionalPenalised";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-scoring-policy-{Guid.NewGuid():N}.db");
    private readonly string assetsPath = Path.Combine(Path.GetTempPath(), $"plancope-assets-{Guid.NewGuid():N}");
    private readonly string connectionString;

    public ExamScoringPolicyPullTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
    }

    [Fact]
    public async Task PullAsync_PersistsScoringPolicy_FromPublishedPackage()
    {
        var connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
        var examRepository = new LocalExamRepository(connectionFactory);
        var syncStateRepository = new SyncStateRepository(connectionFactory);
        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        await syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            "central_url",
            JsonSerializer.Serialize("http://central.test", webOptions),
            DateTimeOffset.UtcNow.ToString("O")));
        await syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            "node_id",
            JsonSerializer.Serialize("node-1", webOptions),
            DateTimeOffset.UtcNow.ToString("O")));

        const string checksum = "sha256-test-checksum";
        var package = new PublishedExamPackageDto(
            "pkg-1",
            "ex-1",
            "ev-1",
            "EXA-2026-01",
            "Matematica · Primer Año",
            2,
            1,
            checksum,
            null,
            new[]
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
            },
            new[]
            {
                new AnswerKeyDto(
                    "ak-1",
                    "blk-1",
                    JsonElementOf("""{"option":"B"}"""),
                    1m,
                    null),
            },
            Array.Empty<PublishedAssetDto>(),
            new[]
            {
                new PublicationTargetDto("grade", "6"),
                new PublicationTargetDto("division", "A"),
                new PublicationTargetDto("subject", "Matematica"),
                new PublicationTargetDto("Global", null),
            },
            ScoringPolicyValue);

        var pull = new PullResponse(
            new[]
            {
                new SyncItem(
                    "publication_package",
                    "pkg-1",
                    "upsert",
                    JsonSerializer.SerializeToElement(package, webOptions),
                    DateTimeOffset.UtcNow.ToString("O"),
                    checksum),
            },
            "100",
            false,
            new Dictionary<string, string>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Local:AssetsPath"] = assetsPath,
            })
            .Build();

        var service = new LocalExamPullService(
            new StubHttpClientFactory(new HttpClient(new StubHandler(() => JsonResponse(pull, webOptions)))),
            syncStateRepository,
            examRepository,
            new LocalAssetFileService(configuration, examRepository));

        var result = await service.PullAsync(CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Imported);

        var stored = await examRepository.GetByIdAsync("ev-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(ScoringPolicyValue, stored!.ScoringPolicy);
    }

    private static JsonElement JsonElementOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage JsonResponse(object value, JsonSerializerOptions options)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, options), Encoding.UTF8, "application/json"),
        };
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(assetsPath))
        {
            Directory.Delete(assetsPath, recursive: true);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder());
        }
    }
}
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
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

/// <summary>
/// Proves the three behaviours required when a second published version of an already-pulled exam
/// arrives: (1) new sessions bind to the current (latest) version, (2) a session that already
/// started keeps the version it started with and its blocks are untouched, and (3) the local exam
/// catalog lists the exam once, as the current version.
/// </summary>
public sealed class LocalExamVersioningTests : IDisposable
{
    private const string ExamCode = "EXA-VER-01";
    private const string V1ExamVersionId = "ev-v1";
    private const string V2ExamVersionId = "ev-v2";
    private const string V1BlockId = "blk-v1";
    private const string V2BlockId = "blk-v2";

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-versioning-{Guid.NewGuid():N}.db");
    private readonly string assetsPath = Path.Combine(Path.GetTempPath(), $"plancope-versioning-assets-{Guid.NewGuid():N}");
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly SyncStateRepository syncStateRepository;
    private readonly LocalExamRepository examRepository;
    private readonly SessionRepository sessionRepository;
    private readonly FakeCentralHandler handler = new();

    public LocalExamVersioningTests()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
        syncStateRepository = new SyncStateRepository(connectionFactory);
        examRepository = new LocalExamRepository(connectionFactory);
        sessionRepository = new SessionRepository(connectionFactory);
    }

    [Fact]
    public async Task Pulling_a_second_version_lists_the_exam_once_as_the_current_version()
    {
        await PullV1ThenStartSessionAsync();

        handler.Packages.Add(Package(V2ExamVersionId, versionNumber: 2, blockId: V2BlockId));
        var second = await PullAsync();

        Assert.True(second.Success, second.Error);
        // Same exam_code already known locally: a new version is an UPDATE, not a new exam.
        Assert.Equal(0, second.NewExams);
        Assert.Equal(1, second.UpdatedExams);

        var catalog = await examRepository.GetExamsAsync();
        var current = Assert.Single(catalog);
        Assert.Equal(V2ExamVersionId, current.Id);
        Assert.Equal(2, current.VersionNumber);
        Assert.Equal(ExamCode, current.ExamCode);
    }

    [Fact]
    public async Task Pulling_a_second_version_keeps_the_existing_session_on_its_original_version_and_blocks()
    {
        var v1Session = await PullV1ThenStartSessionAsync();

        handler.Packages.Add(Package(V2ExamVersionId, versionNumber: 2, blockId: V2BlockId));
        await PullAsync();

        var reloaded = await sessionRepository.GetByIdAsync(v1Session.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(V1ExamVersionId, reloaded!.ExamVersionId);

        // The v1 blocks still belong to v1 and are untouched; v2 has its own separate block set.
        var v1Blocks = await examRepository.GetBlocksAsync(V1ExamVersionId);
        var v1Block = Assert.Single(v1Blocks);
        Assert.Equal(V1BlockId, v1Block.Id);
        Assert.Equal(V1ExamVersionId, v1Block.LocalExamVersionId);
        using (var v1Config = JsonDocument.Parse(v1Block.ConfigJson))
        {
            Assert.Equal("Cuanto es 18 + 24?", v1Config.RootElement.GetProperty("question").GetString());
        }

        var v2Blocks = await examRepository.GetBlocksAsync(V2ExamVersionId);
        var v2Block = Assert.Single(v2Blocks);
        Assert.Equal(V2BlockId, v2Block.Id);

        // The answer keys of the superseded version survive as well.
        var v1AnswerKeys = await examRepository.GetAnswerKeysAsync(V1ExamVersionId);
        Assert.Single(v1AnswerKeys);
    }

    [Fact]
    public async Task New_session_after_pulling_a_second_version_binds_to_the_current_version()
    {
        await PullV1ThenStartSessionAsync();

        handler.Packages.Add(Package(V2ExamVersionId, versionNumber: 2, blockId: V2BlockId));
        await PullAsync();

        var catalog = await examRepository.GetExamsAsync();
        var current = Assert.Single(catalog);
        Assert.Equal(V2ExamVersionId, current.Id);

        var newSession = await CreateSessionAsync(current.Id);
        Assert.Equal(V2ExamVersionId, newSession.ExamVersionId);

        // And the attempt start path resolves the blocks of the current version for that session.
        var blocks = await examRepository.GetBlocksAsync(newSession.ExamVersionId);
        var block = Assert.Single(blocks);
        Assert.Equal(V2BlockId, block.Id);
    }

    private async Task<LocalDeliverySession> PullV1ThenStartSessionAsync()
    {
        await SeedEnrolmentAsync();
        handler.Packages.Add(Package(V1ExamVersionId, versionNumber: 1, blockId: V1BlockId));

        var first = await PullAsync();
        Assert.True(first.Success, first.Error);
        Assert.Equal(1, first.NewExams);
        Assert.Equal(0, first.UpdatedExams);

        var catalog = await examRepository.GetExamsAsync();
        var current = Assert.Single(catalog);
        Assert.Equal(V1ExamVersionId, current.Id);

        return await CreateSessionAsync(current.Id);
    }

    private async Task<LocalDeliverySession> CreateSessionAsync(string examVersionId)
    {
        var session = new LocalDeliverySession(
            Guid.NewGuid().ToString("N"),
            examVersionId,
            "180055400",
            "6 A",
            null,
            "Operador",
            DateTimeOffset.UtcNow.ToString("O"),
            null,
            "active",
            null,
            Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(),
            0);

        await sessionRepository.CreateAsync(session);
        return session;
    }

    private Task<LocalExamPullResult> PullAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Local:AssetsPath"] = assetsPath })
            .Build();

        var service = new LocalExamPullService(
            new StubHttpClientFactory(handler),
            syncStateRepository,
            examRepository,
            new LocalAssetFileService(configuration, examRepository),
            new ExamPullGate());

        return service.PullAsync(CancellationToken.None);
    }

    private async Task SeedEnrolmentAsync()
    {
        await UpsertStateAsync("central_url", "http://central.test");
        await UpsertStateAsync("node_id", "node-1");
    }

    private async Task UpsertStateAsync(string key, string value)
    {
        await syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            JsonSerializer.Serialize(value, WebOptions),
            DateTimeOffset.UtcNow.ToString("O")));
    }

    private static FakePackage Package(string examVersionId, int versionNumber, string blockId)
    {
        return new FakePackage
        {
            ExamVersionId = examVersionId,
            ExamCode = ExamCode,
            VersionNumber = versionNumber,
            BlockId = blockId,
            Ticks = versionNumber * 100L,
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

    private sealed class FakePackage
    {
        public required string ExamVersionId { get; init; }
        public required string ExamCode { get; init; }
        public required int VersionNumber { get; init; }
        public required string BlockId { get; init; }
        public required long Ticks { get; init; }

        public SyncItem ToSyncItem()
        {
            var checksum = $"checksum-{ExamVersionId}";
            var config = JsonSerializer.SerializeToElement(new
            {
                question = VersionNumber == 1 ? "Cuanto es 18 + 24?" : "Cuanto es 20 + 22?",
                options = VersionNumber == 1
                    ? new[] { new { value = "42", label = "42" }, new { value = "44", label = "44" } }
                    : new[] { new { value = "42", label = "42" }, new { value = "43", label = "43" } },
            }, WebOptions);

            var block = new BlockDto(
                BlockId,
                ExamVersionId,
                OrderIndex: 0,
                BlockType.MultipleChoice,
                Title: "Pregunta 1",
                Description: null,
                Config: config,
                Validation: null);

            var answerKey = new AnswerKeyDto(
                $"ak-{ExamVersionId}",
                BlockId,
                JsonSerializer.SerializeToElement(new { option = "42" }, WebOptions),
                1m,
                null);

            var package = new PublishedExamPackageDto(
                $"pkg-{ExamVersionId}",
                $"exam-{ExamCode}",
                ExamVersionId,
                ExamCode,
                "Matematica · Versionado",
                VersionNumber,
                1,
                checksum,
                JsonSerializer.SerializeToElement(new { title = "Matematica", grade = "6" }, WebOptions),
                [block],
                [answerKey],
                Array.Empty<PublishedAssetDto>(),
                [new PublicationTargetDto("grade", "6")],
                null);

            return new SyncItem(
                "publication_package",
                $"pkg-{ExamVersionId}",
                "upsert",
                JsonSerializer.SerializeToElement(package, WebOptions),
                DateTimeOffset.UtcNow.ToString("O"),
                checksum);
        }
    }

    private sealed class FakeCentralHandler : HttpMessageHandler
    {
        public List<FakePackage> Packages { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var cursor = long.TryParse(query["cursor"].ToString(), out var parsedCursor) ? parsedCursor : 0;
            var limit = int.TryParse(query["limit"].ToString(), out var parsedLimit) ? parsedLimit : 50;

            var matching = Packages.Where(package => package.Ticks > cursor).OrderBy(package => package.Ticks).ToList();
            var hasMore = matching.Count > limit;
            var page = matching.Take(limit).ToList();
            var nextCursor = page.Count == 0 ? cursor.ToString() : page[^1].Ticks.ToString();

            var pull = new PullResponse(
                page.Select(item => item.ToSyncItem()).ToList(),
                nextCursor,
                hasMore,
                new Dictionary<string, string>());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(pull, WebOptions), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}

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
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalExamPullServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-pull-{Guid.NewGuid():N}.db");
    private readonly string assetsPath = Path.Combine(Path.GetTempPath(), $"plancope-pull-assets-{Guid.NewGuid():N}");
    private readonly string connectionString;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly SyncStateRepository syncStateRepository;
    private readonly LocalExamRepository examRepository;

    public LocalExamPullServiceTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
        syncStateRepository = new SyncStateRepository(connectionFactory);
        examRepository = new LocalExamRepository(connectionFactory);
    }

    [Fact]
    public async Task Pull_without_node_id_reports_not_enrolled_and_never_calls_central()
    {
        var handler = new FakeCentralHandler([]);
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ExamPullErrorCodes.NotEnrolled, result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
        var response = result.ToResponse();
        Assert.Equal("error", response.Status);
        Assert.Equal("Este equipo todavía no está vinculado con Central.", response.Message);
    }

    [Fact]
    public async Task Pull_reports_central_unreachable_on_transport_failure()
    {
        await SeedEnrolmentAsync();
        var handler = new FakeCentralHandler([]) { Throw = new HttpRequestException("central.test is unreachable") };
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ExamPullErrorCodes.CentralUnreachable, result.ErrorCode);
        Assert.Equal("No se pudo conectar con Central.", result.ToResponse().Message);
        Assert.Equal("true", await ReadStateRawAsync("sync_offline"));
    }

    [Fact]
    public async Task Pull_reports_unauthorized_on_401()
    {
        await SeedEnrolmentAsync();
        var handler = new FakeCentralHandler([]) { ForceStatus = HttpStatusCode.Unauthorized };
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ExamPullErrorCodes.Unauthorized, result.ErrorCode);
    }

    [Fact]
    public async Task First_pull_imports_one_new_exam_with_counts_and_message()
    {
        await SeedEnrolmentAsync();
        var handler = new FakeCentralHandler([FakePackage.New("ev-1", "EXA-1", 100)]);
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.NewExams);
        Assert.Equal(0, result.UpdatedExams);
        Assert.Equal(1, result.TotalReceived);
        Assert.Equal("100", result.Cursor);
        Assert.NotNull(result.LastPullAt);

        var response = result.ToResponse();
        Assert.Equal("updated", response.Status);
        Assert.Equal("Se importó 1 examen nuevo.", response.Message);

        Assert.NotNull(await examRepository.GetByIdAsync("ev-1", CancellationToken.None));
        Assert.Equal("false", await ReadStateRawAsync("sync_offline"));
        Assert.Equal("\"\"", await ReadStateRawAsync("sync_last_error"));
    }

    [Fact]
    public async Task Second_pull_after_cursor_advance_reports_up_to_date()
    {
        await SeedEnrolmentAsync();
        var handler = new FakeCentralHandler([FakePackage.New("ev-1", "EXA-1", 100)]);
        var service = BuildService(handler);

        await service.PullAsync(CancellationToken.None);
        var second = await service.PullAsync(CancellationToken.None);

        Assert.True(second.Success);
        Assert.Equal(0, second.NewExams);
        Assert.Equal(0, second.UpdatedExams);
        Assert.Equal(0, second.TotalReceived);
        Assert.Equal("up_to_date", second.ToResponse().Status);
        Assert.Equal("No hay exámenes nuevos.", second.ToResponse().Message);
    }

    [Fact]
    public async Task Package_added_after_the_cursor_is_imported_as_new()
    {
        await SeedEnrolmentAsync();
        var handler = new FakeCentralHandler([FakePackage.New("ev-1", "EXA-1", 100)]);
        var service = BuildService(handler);

        await service.PullAsync(CancellationToken.None);
        handler.Packages.Add(FakePackage.New("ev-2", "EXA-2", 200));

        var second = await service.PullAsync(CancellationToken.None);

        Assert.True(second.Success);
        Assert.Equal(1, second.NewExams);
        Assert.Equal(0, second.UpdatedExams);
        Assert.Equal("200", second.Cursor);
        Assert.NotNull(await examRepository.GetByIdAsync("ev-2", CancellationToken.None));
    }

    [Fact]
    public async Task Pull_pages_until_has_more_is_false()
    {
        await SeedEnrolmentAsync();
        var packages = Enumerable.Range(1, 51)
            .Select(index => FakePackage.New($"ev-{index}", $"EXA-{index}", index))
            .ToList();
        var handler = new FakeCentralHandler(packages);
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(51, result.NewExams);
        Assert.Equal(51, result.TotalReceived);
        // 50 in the first page (hasMore=true) + 1 in the second (hasMore=false).
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Checksum_mismatch_reports_error_and_stops_import()
    {
        await SeedEnrolmentAsync();
        var package = FakePackage.New("ev-1", "EXA-1", 100);
        package.ItemChecksum = "tampered-checksum";
        var handler = new FakeCentralHandler([package]);
        var service = BuildService(handler);

        var result = await service.PullAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ExamPullErrorCodes.ChecksumMismatch, result.ErrorCode);
        Assert.Null(await examRepository.GetByIdAsync("ev-1", CancellationToken.None));
    }

    private LocalExamPullService BuildService(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Local:AssetsPath"] = assetsPath,
            })
            .Build();

        return new LocalExamPullService(
            new StubHttpClientFactory(handler),
            syncStateRepository,
            examRepository,
            new LocalAssetFileService(configuration, examRepository),
            new ExamPullGate());
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

    private async Task<string?> ReadStateRawAsync(string key)
    {
        var state = await syncStateRepository.GetAsync(key);
        return state?.ValueJson;
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

        public required string Checksum { get; init; }

        public required long Ticks { get; init; }

        public string? ItemChecksum { get; set; }

        public static FakePackage New(string examVersionId, string examCode, long ticks)
        {
            return new FakePackage
            {
                ExamVersionId = examVersionId,
                ExamCode = examCode,
                Checksum = $"checksum-{examVersionId}",
                Ticks = ticks,
            };
        }

        public SyncItem ToSyncItem()
        {
            var package = new PublishedExamPackageDto(
                $"pkg-{ExamVersionId}",
                $"exam-{ExamVersionId}",
                ExamVersionId,
                ExamCode,
                $"Titulo {ExamCode}",
                1,
                1,
                Checksum,
                null,
                Array.Empty<BlockDto>(),
                Array.Empty<AnswerKeyDto>(),
                Array.Empty<PublishedAssetDto>(),
                [new PublicationTargetDto("grade", "6")]);

            return new SyncItem(
                "publication_package",
                $"pkg-{ExamVersionId}",
                "upsert",
                JsonSerializer.SerializeToElement(package, WebOptions),
                DateTimeOffset.UtcNow.ToString("O"),
                ItemChecksum ?? Checksum);
        }
    }

    private sealed class FakeCentralHandler(List<FakePackage> packages) : HttpMessageHandler
    {
        public List<FakePackage> Packages { get; } = packages;

        public int CallCount { get; private set; }

        public Exception? Throw { get; set; }

        public HttpStatusCode? ForceStatus { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (Throw is not null)
            {
                return Task.FromException<HttpResponseMessage>(Throw);
            }

            if (ForceStatus is { } forced)
            {
                return Task.FromResult(new HttpResponseMessage(forced));
            }

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
        // Mirror the real IHttpClientFactory: hand out a fresh wrapper per call so callers may
        // mutate BaseAddress without tripping HttpClient's "already started" guard.
        public HttpClient CreateClient(string name) => new(handler);
    }
}

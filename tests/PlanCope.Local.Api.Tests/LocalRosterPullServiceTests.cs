using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalRosterPullServiceTests
{
    [Fact]
    public async Task Full_pull_skips_invalid_index_cues_and_imports_the_remaining_data()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-roster-pull-{Guid.NewGuid():N}.db");
        try
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            var options = new LocalDatabaseOptions(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
            new LocalDatabaseInitializer(options).Initialize();
            var connections = new LocalSqliteConnectionFactory(options);
            var state = new SyncStateRepository(connections);
            await state.UpsertAsync(State("central_url", "https://central.example"));
            await state.UpsertAsync(State("node_id", "node-1"));
            var index = new
            {
                serverTime = DateTimeOffset.UtcNow,
                schools = new[]
                {
                    new { cue = "bad-cue", name = "Ignorada" },
                    new { cue = "180000001", name = "Escuela válida" }
                },
                rosters = new[]
                {
                    new { cue = "invalid", schoolYear = "2026" },
                    new { cue = "180000001", schoolYear = "2026" }
                }
            };
            var package = MakePackage();
            var factory = new StubHttpClientFactory(request => request.RequestUri!.AbsolutePath.EndsWith("rosters/index", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(index) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(package) });
            var repository = new LocalRosterRepository(connections, NullLogger<LocalRosterRepository>.Instance);
            var hmac = new DocumentHmacService(Options.Create(new NominalizationOptions
            {
                DocumentHmacKey = "release-test-key-with-at-least-32-bytes"
            }));
            var service = new LocalRosterPullService(factory, state, repository, hmac,
                NullLogger<LocalRosterPullService>.Instance);

            var result = await service.PullAllAsync();

            Assert.True(result.Success, result.Error);
            Assert.Equal(1, result.Downloaded);
            Assert.Equal(2, result.Total);
            Assert.Equal(2, result.Skipped);
            using var connection = connections.CreateOpenConnection();
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schools WHERE cue='180000001';"));
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schools WHERE name='Ignorada';"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_roster_snapshots WHERE cue='180000001';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static SyncState State(string key, string value) => new(Guid.NewGuid().ToString("N"), key,
        JsonSerializer.Serialize(value), DateTimeOffset.UtcNow.ToString("O"));

    private static GeRosterPackageDto MakePackage()
    {
        var package = new GeRosterPackageDto("snapshot-1", "180000001", "2026", DateTimeOffset.UtcNow,
            string.Empty, 0, 0, "Synced", []);
        return package with { Checksum = GeRosterPackageChecksum.Calculate(package) };
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responseFactory));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}

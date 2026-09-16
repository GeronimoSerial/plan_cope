using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class NodeIdentityRepositoryTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-node-identity-{Guid.NewGuid():N}.db");
    private readonly string connectionString;
    private readonly NodeIdentityRepository repository;

    public NodeIdentityRepositoryTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        repository = new NodeIdentityRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var identity = await repository.GetAsync();

        Assert.Null(identity);
    }

    [Fact]
    public async Task UpsertThenGet_RoundTripsEveryField()
    {
        SeedSchool("180055400");

        var expected = new NodeIdentity(
            Id: "node-identity-1",
            NodeId: null,
            Cue: "180055400",
            FingerprintHash: "sha256-abcdef",
            FingerprintComponentsJson: """{"cpu":"x"}""",
            EnrolledAt: null,
            LastSyncAt: "2026-09-15 10:00:00",
            CredentialState: "unenrolled",
            RevocationDetectedAt: null,
            RevocationStage: null);

        await repository.UpsertAsync(expected);
        var actual = await repository.GetAsync();

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task UpsertTwice_UpdatesExistingRow_InsteadOfInsertingSecond()
    {
        SeedSchool("180055400");

        var first = new NodeIdentity(
            Id: "node-identity-1",
            NodeId: null,
            Cue: "180055400",
            FingerprintHash: "sha256-abcdef",
            FingerprintComponentsJson: """{"cpu":"x"}""",
            EnrolledAt: null,
            LastSyncAt: null,
            CredentialState: "unenrolled",
            RevocationDetectedAt: null,
            RevocationStage: null);

        await repository.UpsertAsync(first);

        var second = first with
        {
            Id = "node-identity-2",
            NodeId = "node-123",
            FingerprintHash = "sha256-123456",
            EnrolledAt = "2026-09-15 11:00:00",
            LastSyncAt = "2026-09-15 11:00:00",
            CredentialState = "active",
            RevocationDetectedAt = "2026-09-15 11:30:00",
            RevocationStage = "detected"
        };

        await repository.UpsertAsync(second);

        var actual = await repository.GetAsync();
        Assert.NotNull(actual);
        Assert.Equal("node-identity-1", actual!.Id);
        Assert.Equal("node-123", actual.NodeId);
        Assert.Equal("180055400", actual.Cue);
        Assert.Equal("sha256-123456", actual.FingerprintHash);
        Assert.Equal("active", actual.CredentialState);
        Assert.Equal("2026-09-15 11:00:00", actual.EnrolledAt);
        Assert.Equal("2026-09-15 11:30:00", actual.RevocationDetectedAt);
        Assert.Equal("detected", actual.RevocationStage);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM node_identity;";
        var count = command.ExecuteScalar();
        Assert.NotNull(count);
        Assert.Equal(1, Convert.ToInt64(count));
    }

    private void SeedSchool(string cue)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO schools (cue, name) VALUES (@Cue, NULL);";
        command.Parameters.AddWithValue("@Cue", cue);
        command.ExecuteNonQuery();
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
    }
}
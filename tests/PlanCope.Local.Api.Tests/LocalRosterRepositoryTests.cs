using Microsoft.Data.Sqlite;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalRosterRepositoryTests
{
    [Fact]
    public async Task Fresh_install_generates_a_persistent_key_and_imports_rosters_without_configuration()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-roster-key-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var connections = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
            var repository = new LocalRosterRepository(connections, NullLogger<LocalRosterRepository>.Instance);
            var package = CreatePackage();
            var firstService = new DocumentHmacService(Options.Create(new NominalizationOptions()), connections);

            var imported = await repository.ImportAsync(package, firstService);

            Assert.True(imported.Imported);
            using var connection = connections.CreateOpenConnection();
            var persistedHash = await connection.QuerySingleAsync<string>(
                "SELECT document_hash FROM local_roster_students WHERE ge_person_id = 101;");
            Assert.Equal(64, persistedHash.Length);
            Assert.DoesNotContain("12345678", persistedHash, StringComparison.Ordinal);

            var secondService = new DocumentHmacService(Options.Create(new NominalizationOptions()), connections);
            Assert.Equal(persistedHash, secondService.ComputeHash("12.345.678"));
            var lookup = await repository.FindStudentAsync(package.SnapshotId, "section-a", "12.345.678", secondService);
            Assert.Equal(101, lookup?.GePersonId);
            Assert.Equal("5678", lookup?.DocumentLast4);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sync_state WHERE key='document_hmac_key';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public void Configured_key_overrides_the_persisted_installation_key()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-roster-override-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var connections = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
            var generated = new DocumentHmacService(Options.Create(new NominalizationOptions()), connections);
            var generatedHash = generated.ComputeHash("12345678");
            var configuredKey = "configured-hmac-key-that-is-longer-than-thirty-two-bytes";
            var configured = new DocumentHmacService(Options.Create(new NominalizationOptions { DocumentHmacKey = configuredKey }), connections);

            var configuredHash = configured.ComputeHash("12345678");

            Assert.NotEqual(generatedHash, configuredHash);
            Assert.Equal(new DocumentHmacService(Options.Create(new NominalizationOptions { DocumentHmacKey = configuredKey }))
                .ComputeHash("12345678"), configuredHash);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Import_is_idempotent_supports_multiple_sections_and_lookup_uses_hmac_without_full_document()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-roster-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var repository = new LocalRosterRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)),
                NullLogger<LocalRosterRepository>.Instance);
            var hmac = new DocumentHmacService(Options.Create(new NominalizationOptions { DocumentHmacKey = "release-test-key-with-at-least-32-bytes" }));
            var package = CreatePackage();

            var first = await repository.ImportAsync(package with { SchoolName = null }, hmac);
            var second = await repository.ImportAsync(package, hmac);
            var latestSnapshot = await repository.GetLatestSnapshotAsync(package.Cue, package.SchoolYear);
            var latestSnapshotWithoutYear = await repository.GetLatestSnapshotAsync(package.Cue);
            var sections = await repository.GetSectionsAsync(package.Cue, package.SchoolYear);
            var firstSectionLookup = await repository.FindStudentAsync(package.SnapshotId, "section-a", "12.345.678", hmac);
            var secondSectionLookup = await repository.FindStudentAsync(package.SnapshotId, "section-b", "12.345.678", hmac);

            Assert.True(first.Imported);
            Assert.False(second.Imported);
            Assert.Equal(2, first.SectionCount);
            Assert.Equal(2, first.StudentCount);
            Assert.NotNull(latestSnapshot);
            Assert.Equal(2, latestSnapshot!.SectionCount);
            Assert.Equal(2, latestSnapshot.StudentCount);
            Assert.Equal("Escuela Primaria 123", latestSnapshot.SchoolName);
            Assert.Equal(latestSnapshot, latestSnapshotWithoutYear);
            Assert.Equal(2, sections.Count);
            Assert.All(sections, section => Assert.Equal(1, section.StudentCount));
            Assert.NotNull(firstSectionLookup);
            Assert.Equal(101, firstSectionLookup!.GePersonId);
            Assert.NotNull(secondSectionLookup);
            Assert.Equal(102, secondSectionLookup!.GePersonId);
            Assert.Equal("5678", firstSectionLookup.DocumentLast4);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'local_roster_students';";
            var schema = (string?)command.ExecuteScalar();
            Assert.DoesNotContain("document TEXT", schema, StringComparison.OrdinalIgnoreCase);

            command.CommandText = "SELECT document_hash, document_last4, first_name, last_name FROM local_roster_students;";
            using var reader = command.ExecuteReader();
            var rows = 0;
            while (reader.Read())
            {
                rows++;
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    Assert.DoesNotContain("12345678", reader.GetString(index), StringComparison.Ordinal);
                }
            }

            Assert.Equal(2, rows);
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static GeRosterPackageDto CreatePackage()
    {
        var sections = new[]
        {
            new GeRosterSectionPackageDto("section-a", 100, "6º", "A", "Primario", "Mañana", new[]
            {
                new GeRosterStudentPackageDto("student-a", "section-a", 101, "12.345.678", "Ana", "Pérez")
            }),
            new GeRosterSectionPackageDto("section-b", 200, "6º", "B", "Primario", "Mañana", new[]
            {
                new GeRosterStudentPackageDto("student-b", "section-b", 102, "12.345.678", "Luis", "Gómez")
            })
        };
        var package = new GeRosterPackageDto(
            "snapshot-1",
            "180055400",
            "2026",
            DateTimeOffset.UtcNow,
            "",
            2,
            2,
            "Ready",
            sections,
            "Escuela Primaria 123");
        return package with { Checksum = GeRosterPackageChecksum.Calculate(package) };
    }

    [Theory]
    [InlineData("")]
    [InlineData("short-key")]
    public void Hmac_key_must_have_minimum_entropy(string key)
    {
        var hmac = new DocumentHmacService(Options.Create(new NominalizationOptions { DocumentHmacKey = key }));

        Assert.Throws<InvalidOperationException>(() => hmac.ComputeHash("12345678"));
    }
}

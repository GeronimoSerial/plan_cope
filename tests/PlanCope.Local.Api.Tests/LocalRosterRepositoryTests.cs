using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalRosterRepositoryTests
{
    [Fact]
    public async Task Import_is_idempotent_supports_multiple_sections_and_lookup_uses_hmac_without_full_document()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-roster-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var repository = new LocalRosterRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)));
            var hmac = new DocumentHmacService(Options.Create(new NominalizationOptions { DocumentHmacKey = "release-test-key-with-at-least-32-bytes" }));
            var package = CreatePackage();

            var first = await repository.ImportAsync(package, hmac);
            var second = await repository.ImportAsync(package, hmac);
            var firstSectionLookup = await repository.FindStudentAsync(package.SnapshotId, "section-a", "12.345.678", hmac);
            var secondSectionLookup = await repository.FindStudentAsync(package.SnapshotId, "section-b", "12.345.678", hmac);

            Assert.True(first.Imported);
            Assert.False(second.Imported);
            Assert.Equal(2, first.SectionCount);
            Assert.Equal(2, first.StudentCount);
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
        var package = new GeRosterPackageDto("snapshot-1", "1800554-00", "2026", DateTimeOffset.UtcNow, "", 2, 2, "Ready", sections);
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

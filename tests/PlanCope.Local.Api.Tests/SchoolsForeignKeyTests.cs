using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class SchoolsForeignKeyTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-schools-fk-{Guid.NewGuid():N}.db");
    private readonly string connectionString;

    public SchoolsForeignKeyTests()
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
    }

    [Fact]
    public void Migration008_BackfillsSchoolsAndEnforcesForeignKeys()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, schema_version, synced_at)
                VALUES ('exam-version-1', 'remote-exam-version-1', 'EXAM-1', 1, 'exam-checksum-1', 1, datetime('now'));

                INSERT INTO schools (cue, name) VALUES ('180055400', 'Escuela Primaria 123');

                INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
                VALUES ('snapshot-1', '180055400', '2026', datetime('now'), 'snapshot-checksum-1', 2, 2, 'Ready');

                INSERT INTO delivery_sessions (id, exam_version_id, school_code, started_by, start_at, status)
                VALUES ('session-1', 'exam-version-1', '180055400', 'user-1', datetime('now'), 'Pending');
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            var violations = 0;
            while (reader.Read())
            {
                violations++;
            }

            Assert.Equal(0, violations);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO delivery_sessions (id, exam_version_id, school_code, started_by, start_at, status)
                VALUES ('session-orphan', 'exam-version-1', 'NOT-A-SCHOOL', 'user-1', datetime('now'), 'Pending');
                """;
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }
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

    [Fact]
    public async Task CreateAsync_UpsertsSchoolsRow_WhenSchoolHasNoRosterSnapshot()
    {
        const string cue = "180077700";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, schema_version, synced_at)
                VALUES ('exam-version-2', 'remote-exam-version-2', 'EXAM-2', 1, 'exam-checksum-2', 1, datetime('now'));
                """;
            command.ExecuteNonQuery();
        }

        var repository = new SessionRepository(new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString)));
        var session = new LocalDeliverySession(
            Id: "session-offline-first",
            ExamVersionId: "exam-version-2",
            SchoolCode: cue,
            ClassroomCode: null,
            CommissionCode: null,
            StartedBy: "user-1",
            StartAt: "2026-09-15 08:00:00",
            EndAt: null,
            Status: "Pending",
            ConfigJson: null,
            AccessCode: "OFFLINE1",
            ExpectedStudentCount: 0,
            SchoolYear: "2026",
            RosterSnapshotId: null,
            RosterSectionId: null);

        await repository.CreateAsync(session);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT cue FROM schools WHERE cue = @Cue;";
            command.Parameters.AddWithValue("@Cue", cue);
            var storedCue = command.ExecuteScalar() as string;
            Assert.Equal(cue, storedCue);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            var violations = 0;
            while (reader.Read())
            {
                violations++;
            }

            Assert.Equal(0, violations);
        }
    }
}
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

/// <summary>
/// Proves that the operator-facing catalog applies the grade filter AFTER ranking versions per
/// exam_code. Filtering inside the ROW_NUMBER subquery dropped versions of other grades before
/// ranking, which could surface a superseded version as the current one.
/// </summary>
public sealed class LocalExamCatalogGradeFilterTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-grade-filter-{Guid.NewGuid():N}.db");
    private readonly LocalExamRepository examRepository;

    public LocalExamCatalogGradeFilterTests()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        var connectionFactory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
        examRepository = new LocalExamRepository(connectionFactory);
    }

    [Fact]
    public async Task Grade_filter_does_not_return_a_superseded_version_when_the_current_version_has_another_grade()
    {
        await SeedExamAsync("EXA-01", "ev-01-v2", versionNumber: 2, grade: "6");
        await SeedExamAsync("EXA-01", "ev-01-v3", versionNumber: 3, grade: "5");

        // v3 (grade "5") is the current version, so filtering by the superseded grade "6" returns nothing.
        var filteredBySix = await examRepository.GetExamsAsync("6");
        Assert.Empty(filteredBySix);

        // The current version keeps its own grade and is returned by that filter.
        var filteredByFive = await examRepository.GetExamsAsync("5");
        var byFive = Assert.Single(filteredByFive);
        Assert.Equal("ev-01-v3", byFive.Id);
        Assert.Equal(3, byFive.VersionNumber);

        // No filter still exposes only the current version.
        var unfiltered = await examRepository.GetExamsAsync();
        var current = Assert.Single(unfiltered);
        Assert.Equal("ev-01-v3", current.Id);
    }

    [Fact]
    public async Task Grade_filter_selects_current_versions_across_exams_without_touching_other_exams()
    {
        await SeedExamAsync("EXA-A", "ev-a-v1", versionNumber: 1, grade: "6");
        await SeedExamAsync("EXA-B", "ev-b-v1", versionNumber: 1, grade: "5");
        await SeedExamAsync("EXA-B", "ev-b-v2", versionNumber: 2, grade: "6");

        // EXA-A stays on grade "6"; EXA-B moved from "5" to "6" on its current v2.
        var filteredBySix = await examRepository.GetExamsAsync("6");
        Assert.Equal(2, filteredBySix.Count);
        Assert.Contains(filteredBySix, exam => exam.Id == "ev-a-v1");
        Assert.Contains(filteredBySix, exam => exam.Id == "ev-b-v2");
        Assert.DoesNotContain(filteredBySix, exam => exam.Id == "ev-b-v1");

        // The superseded grade "5" no longer matches any current version.
        var filteredByFive = await examRepository.GetExamsAsync("5");
        Assert.Empty(filteredByFive);
    }

    [Fact]
    public async Task Blank_grade_filter_behaves_like_no_filter()
    {
        await SeedExamAsync("EXA-01", "ev-01-v2", versionNumber: 2, grade: "6");
        await SeedExamAsync("EXA-01", "ev-01-v3", versionNumber: 3, grade: "5");

        var blank = await examRepository.GetExamsAsync("   ");
        var current = Assert.Single(blank);
        Assert.Equal("ev-01-v3", current.Id);
    }

    [Fact]
    public async Task Grade_filter_matches_any_course_in_a_multi_course_exam()
    {
        var exam = new LocalExamVersion(
            "ev-multi", "remote-multi", "EXA-MULTI", 1, "checksum",
            JsonSerializer.Serialize(new { title = "Exam", grade = new[] { "primaria-6", "secundaria-1" } }),
            SchemaVersion: 1, SyncedAt: "2026-01-01T00:00:00.0000000+00:00");
        await examRepository.UpsertImportedExamAsync(exam, [], [], []);

        Assert.Equal("ev-multi", Assert.Single(await examRepository.GetExamsAsync("primaria-6")).Id);
        Assert.Equal("ev-multi", Assert.Single(await examRepository.GetExamsAsync("secundaria-1")).Id);
        Assert.Empty(await examRepository.GetExamsAsync("primaria-5"));
    }

    private async Task SeedExamAsync(string examCode, string id, int versionNumber, string grade)
    {
        var exam = new LocalExamVersion(
            id,
            $"remote-{id}",
            examCode,
            versionNumber,
            $"checksum-{id}",
            JsonSerializer.Serialize(new { title = $"Exam {examCode}", grade }),
            SchemaVersion: 1,
            SyncedAt: "2026-01-01T00:00:00.0000000+00:00");

        await examRepository.UpsertImportedExamAsync(exam, [], [], []);
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

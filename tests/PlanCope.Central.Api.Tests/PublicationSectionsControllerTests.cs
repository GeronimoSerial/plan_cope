using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class PublicationSectionsControllerTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task GetOptions_UnionsLatestActiveTargetRostersAndSeparatesShifts()
    {
        using var dbContext = new PlanCopeDbContext(CreateOptions());
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        dbContext.RegisteredNodes.AddRange(Node("node-1", "180000100", "Active", null), Node("node-2", "180000200", "Active", null),
            Node("node-revoked", "180000300", "Active", now), Node("node-inactive", "180000400", "Inactive", null));
        dbContext.GeRosterSnapshots.AddRange(
            Snapshot("current-1", "180000100", "2026", now),
            Snapshot("older-1", "180000100", "2026", now.AddDays(-1)),
            Snapshot("current-2", "180000200", "2026", now.AddHours(-1)),
            Snapshot("revoked", "180000300", "2026", now),
            Snapshot("inactive", "180000400", "2026", now),
            Snapshot("old-year", "180000100", "2025", now.AddDays(1)));
        dbContext.GeRosterSections.AddRange(
            Section("s1", "current-1", "1º", "Primario", "A", "Mañana"),
            Section("s2", "current-2", "1º", "Primario", "A", "Tarde"),
            Section("s3", "current-2", "1º", "Secundario", "B", "Tarde"),
            Section("old-snapshot", "older-1", "1º", "Primario", "C", "Mañana"),
            Section("revoked-snapshot", "revoked", "1º", "Primario", "D", "Mañana"),
            Section("inactive-snapshot", "inactive", "1º", "Primario", "E", "Mañana"),
            Section("old-year-snapshot", "old-year", "1º", "Primario", "F", "Mañana"));
        await dbContext.SaveChangesAsync();

        var controller = new PublicationSectionsController(dbContext, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rosters:SchoolYear"] = "2026" })
            .Build());

        var action = await controller.GetOptions(["primaria-1", "secundaria-1"], CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var options = Assert.IsAssignableFrom<IReadOnlyList<PublicationSectionOptionDto>>(result.Value);
        Assert.Equal(new[] { "A · Mañana", "A · Tarde" }, options.Where(option => option.GradeValue == "primaria-1").Select(option => option.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal("secundaria-1", Assert.Single(options, option => option.Value == "B").GradeValue);
        Assert.DoesNotContain(options, option => new[] { "C", "D", "E", "F" }.Contains(option.Label, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetOptions_RejectsUnsupportedExamGrades()
    {
        using var dbContext = new PlanCopeDbContext(CreateOptions());
        var controller = new PublicationSectionsController(dbContext, new ConfigurationBuilder().Build());

        var action = await controller.GetOptions(["grado-inventado"], CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions() => new DbContextOptionsBuilder<PlanCopeDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .UseInternalServiceProvider(InMemoryServices)
        .Options;

    private static RegisteredNode Node(string id, string cue, string status, DateTimeOffset? revokedAt) => new(
        id, null, id, null, status, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, id,
        JsonDocument.Parse("{}"), cue, null, DateTimeOffset.UtcNow, revokedAt, null);

    private static GeRosterSnapshot Snapshot(string id, string cue, string year, DateTimeOffset fetchedAt) => new()
    {
        Id = id,
        Cue = cue,
        SchoolYear = year,
        FetchedAt = fetchedAt,
        Checksum = id,
        Status = "Synced"
    };

    private static GeRosterSection Section(string id, string snapshotId, string course, string level, string division, string shift) => new()
    {
        Id = id,
        SnapshotId = snapshotId,
        Course = course,
        Level = level,
        Division = division,
        Shift = shift
    };
}

using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Domain.Central;
using Xunit;
using GeApiRosterStudent = PlanCope.Central.Api.Integrations.Ge.GeRosterStudent;

namespace PlanCope.Central.Api.Tests;

public sealed class GeRosterServiceTests
{
    [Fact]
    public async Task Refresh_IsSequentiallyIdempotentAndKeepsOneSnapshot()
    {
        var store = new FakeRosterStore();
        var apiClient = new FakeGeApiClient([Student(11, 100, "12345678")]);
        var service = new GeRosterService(store, apiClient);

        var first = await service.RefreshAsync("1800554-00", "2026");
        var second = await service.RefreshAsync("1800554-00", "2026");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.SnapshotId, second.SnapshotId);
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Single(store.Snapshots);
        Assert.Single(store.Snapshots[0].Sections);
        Assert.Single(store.Snapshots[0].Sections.Single().Students);
    }

    [Fact]
    public async Task Refresh_RejectsEmptyRosterAndPreservesPreviousSnapshot()
    {
        var store = new FakeRosterStore();
        var apiClient = new FakeGeApiClient([Student(11, 100, "12345678")]);
        var service = new GeRosterService(store, apiClient);
        var previous = await service.RefreshAsync("1800554-00", "2026");
        apiClient.Students = [];

        await Assert.ThrowsAsync<GeRosterEmptyException>(() => service.RefreshAsync("1800554-00", "2026"));

        var current = await service.GetLatestAsync("1800554-00", "2026");
        Assert.NotNull(current);
        Assert.Equal(previous.SnapshotId, current.Id);
        Assert.Equal(previous.Checksum, current.Checksum);
        Assert.Single(store.Snapshots);
    }

    private static GeApiRosterStudent Student(int personId, int sectionId, string document)
    {
        return new GeApiRosterStudent(personId, sectionId, "1800554-00", "6º", "A", "Primario", "Mañana", "PEREZ", "ANA", document);
    }

    private sealed class FakeGeApiClient(IReadOnlyList<GeApiRosterStudent> students) : IGeApiClient
    {
        public IReadOnlyList<GeApiRosterStudent> Students { get; set; } = students;

        public Task<GeStudentIdentity?> FindStudentByDocumentAsync(string document, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<GeApiRosterStudent>> GetStudentsBySchoolAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Students);
        }
    }

    private sealed class FakeRosterStore : IGeRosterStore
    {
        public List<GeRosterSnapshot> Snapshots { get; } = [];

        public Task<GeRosterSnapshot?> FindByChecksumAsync(string cue, string schoolYear, string checksum, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Snapshots.SingleOrDefault(snapshot =>
                snapshot.Cue == cue && snapshot.SchoolYear == schoolYear && snapshot.Checksum == checksum));
        }

        public Task AddAsync(GeRosterSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (Snapshots.Any(existing => existing.Cue == snapshot.Cue && existing.SchoolYear == snapshot.SchoolYear && existing.Checksum == snapshot.Checksum))
            {
                throw new DbUpdateException("duplicate checksum");
            }

            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Snapshots
                .Where(snapshot => snapshot.Cue == cue && snapshot.SchoolYear == schoolYear)
                .OrderByDescending(snapshot => snapshot.FetchedAt)
                .ThenByDescending(snapshot => snapshot.Id)
                .FirstOrDefault());
        }

        public Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            var snapshot = Snapshots.Single(snapshot => snapshot.Id == snapshotId);
            IReadOnlyList<GeRosterSectionStatus> sections = snapshot.Sections
                .Select(section => new GeRosterSectionStatus(
                    section.Id,
                    section.GeSectionId,
                    section.Course,
                    section.Division,
                    section.Level,
                    section.Shift,
                    section.Students.Count))
                .ToList();
            return Task.FromResult(sections);
        }
    }
}

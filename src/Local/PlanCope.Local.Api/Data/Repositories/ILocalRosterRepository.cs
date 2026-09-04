using PlanCope.Shared.Contracts.Sync;
using PlanCope.Local.Api.Services;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ILocalRosterRepository
{
    Task<LocalRosterImportResult> ImportAsync(
        GeRosterPackageDto package,
        IDocumentHmacService documentHmacService,
        CancellationToken cancellationToken = default);

    Task<LocalRosterStudentLookup?> FindStudentAsync(
        string snapshotId,
        string sectionId,
        string document,
        IDocumentHmacService documentHmacService,
        CancellationToken cancellationToken = default);

    Task<LocalRosterSnapshotLookup?> GetLatestSnapshotAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default);

    Task<LocalRosterSnapshotLookup?> GetLatestSnapshotAsync(
        string cue,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalRosterSectionLookup>> GetSectionsAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default);

    Task<LocalRosterSelectionValidation> ValidateSelectionAsync(
        string cue,
        string schoolYear,
        string snapshotId,
        string sectionId,
        CancellationToken cancellationToken = default);
}

public sealed record LocalRosterImportResult(
    bool Imported,
    string SnapshotId,
    string Checksum,
    int SectionCount,
    int StudentCount);

public sealed class LocalRosterStudentLookup
{
    public string SnapshotId { get; set; } = string.Empty;

    public string SectionId { get; set; } = string.Empty;

    public string RosterStudentId { get; set; } = string.Empty;

    public int GePersonId { get; set; }

    public string DocumentLast4 { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}

public sealed record LocalRosterSnapshotLookup(
    string Id,
    string Cue,
    string SchoolYear,
    string FetchedAt,
    string Checksum,
    int SectionCount,
    int StudentCount,
    string Status,
    string? SchoolName = null);

public sealed record LocalRosterSectionLookup(
    string Id,
    string SnapshotId,
    int? GeSectionId,
    string? Course,
    string? Division,
    string? Level,
    string? Shift,
    int StudentCount);

public sealed record LocalRosterSelectionValidation(bool IsValid, string? Error, int? StudentCount = null);

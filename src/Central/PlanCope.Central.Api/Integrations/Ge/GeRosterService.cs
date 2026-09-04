using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using GeApiRosterStudent = PlanCope.Central.Api.Integrations.Ge.GeRosterStudent;
using GeDomainRosterStudent = PlanCope.Shared.Domain.Central.GeRosterStudent;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Integrations.Ge;

public interface IGeRosterService
{
    Task<GeRosterRefreshResult> RefreshAsync(string cue, string schoolYear, CancellationToken cancellationToken = default);

    Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default);
}

public interface IGeRosterStore
{
    Task<GeRosterSnapshot?> FindByChecksumAsync(string cue, string schoolYear, string checksum, CancellationToken cancellationToken = default);

    Task AddAsync(GeRosterSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string snapshotId, CancellationToken cancellationToken = default);
}

public sealed class EfGeRosterStore(PlanCopeDbContext dbContext) : IGeRosterStore
{
    public Task<GeRosterSnapshot?> FindByChecksumAsync(string cue, string schoolYear, string checksum, CancellationToken cancellationToken = default)
    {
        return dbContext.GeRosterSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot =>
                snapshot.Cue == cue &&
                snapshot.SchoolYear == schoolYear &&
                snapshot.Checksum == checksum,
                cancellationToken);
    }

    public async Task AddAsync(GeRosterSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        dbContext.GeRosterSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<GeRosterSnapshot?> GetLatestAsync(string cue, string schoolYear, CancellationToken cancellationToken = default)
    {
        return dbContext.GeRosterSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Cue == cue && snapshot.SchoolYear == schoolYear)
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeRosterSections
            .AsNoTracking()
            .Where(section => section.SnapshotId == snapshotId)
            .OrderBy(section => section.Course)
            .ThenBy(section => section.Division)
            .ThenBy(section => section.Id)
            .Select(section => new GeRosterSectionStatus(
                section.Id,
                section.GeSectionId,
                section.Course,
                section.Division,
                section.Level,
                section.Shift,
                section.Students.Count))
            .ToListAsync(cancellationToken);
    }
}

public sealed record GeRosterRefreshResult(
    string SnapshotId,
    string Cue,
    string SchoolYear,
    DateTimeOffset FetchedAt,
    string Checksum,
    int SectionCount,
    int StudentCount,
    string Status,
    bool Created);

public sealed record GeRosterSectionStatus(
    string Id,
    int? GeSectionId,
    string? Course,
    string? Division,
    string? Level,
    string? Shift,
    int StudentCount);

public sealed record GeRosterBuildResult(
    string Cue,
    string SchoolYear,
    string Checksum,
    IReadOnlyList<GeRosterSectionBuild> Sections,
    int StudentCount);

public sealed record GeRosterSectionBuild(
    string Key,
    int? GeSectionId,
    string? Course,
    string? Division,
    string? Level,
    string? Shift,
    IReadOnlyList<GeRosterStudentBuild> Students);

public sealed record GeRosterStudentBuild(
    int GePersonId,
    string Document,
    string FirstName,
    string LastName);

public sealed class GeRosterEmptyException(string cue, string schoolYear)
    : InvalidOperationException($"GE returned an empty roster for CUE '{cue}' and school year '{schoolYear}'.");

public static class GeRosterSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GeRosterBuildResult Build(
        string cue,
        string schoolYear,
        IEnumerable<GeApiRosterStudent> source)
    {
        var normalizedCue = CueCode.Normalize(cue);
        var normalizedSchoolYear = NormalizeText(schoolYear);
        if (string.IsNullOrWhiteSpace(normalizedSchoolYear))
        {
            throw new ArgumentException("School year is required.", nameof(schoolYear));
        }

        var rows = source
            .Where(static student => student.PersonaId > 0)
            .Select(student => NormalizeStudent(student, normalizedCue))
            .GroupBy(static student => student.IdentityKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static student => student.SectionKey, StringComparer.Ordinal)
            .ThenBy(static student => student.GePersonId)
            .ThenBy(static student => student.Document, StringComparer.Ordinal)
            .ThenBy(static student => student.LastName, StringComparer.Ordinal)
            .ThenBy(static student => student.FirstName, StringComparer.Ordinal)
            .ToList();

        var sections = rows
            .GroupBy(static student => student.SectionKey, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group =>
            {
                var first = group.First();
                return new GeRosterSectionBuild(
                    group.Key,
                    first.GeSectionId,
                    first.Course,
                    first.Division,
                    first.Level,
                    first.Shift,
                    group
                        .Select(static student => new GeRosterStudentBuild(student.GePersonId, student.Document, student.FirstName, student.LastName))
                        .ToList());
            })
            .ToList();

        var canonicalRows = rows.Select(static student => new CanonicalRosterRow(
            student.SectionKey,
            student.GeSectionId,
            student.Cue,
            student.Course,
            student.Division,
            student.Level,
            student.Shift,
            student.GePersonId,
            student.Document,
            student.FirstName,
            student.LastName));
        var canonical = JsonSerializer.Serialize(canonicalRows, JsonOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return new GeRosterBuildResult(normalizedCue, normalizedSchoolYear, checksum, sections, rows.Count);
    }

    private static NormalizedRosterStudent NormalizeStudent(GeApiRosterStudent student, string normalizedCue)
    {
        // The requested CUE is the identity of the snapshot. CueAnexo is an
        // optional GE field and may be absent or formatted inconsistently.
        var cue = normalizedCue;
        var course = NormalizeNullableText(student.Curso);
        var division = NormalizeNullableText(student.Division);
        var level = NormalizeNullableText(student.NivelEnsenanza);
        var shift = NormalizeNullableText(student.Turno);
        var sectionKey = student.EstablecimientoCursoDivisionId.HasValue
            ? $"id:{student.EstablecimientoCursoDivisionId.Value}"
            : string.Join('|', "fields", cue, course ?? string.Empty, division ?? string.Empty, level ?? string.Empty, shift ?? string.Empty);
        var document = new string((student.NroDocumento ?? string.Empty).Where(char.IsDigit).ToArray());
        var firstName = NormalizeText(student.Nombre);
        var lastName = NormalizeText(student.Apellido);
        var identityKey = string.Join('|', sectionKey, student.PersonaId);
        return new NormalizedRosterStudent(
            identityKey,
            sectionKey,
            student.EstablecimientoCursoDivisionId,
            cue,
            course,
            division,
            level,
            shift,
            student.PersonaId,
            document,
            firstName,
            lastName);
    }

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeNullableText(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed record NormalizedRosterStudent(
        string IdentityKey,
        string SectionKey,
        int? GeSectionId,
        string Cue,
        string? Course,
        string? Division,
        string? Level,
        string? Shift,
        int GePersonId,
        string Document,
        string FirstName,
        string LastName);

    private sealed record CanonicalRosterRow(
        string SectionKey,
        int? GeSectionId,
        string Cue,
        string? Course,
        string? Division,
        string? Level,
        string? Shift,
        int GePersonId,
        string Document,
        string FirstName,
        string LastName);
}

public sealed class GeRosterService(
    IGeRosterStore rosterStore,
    IGeApiClient geApiClient,
    TimeProvider? timeProvider = null) : IGeRosterService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<GeRosterRefreshResult> RefreshAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var students = await geApiClient.GetStudentsBySchoolAsync(cue, schoolYear, cancellationToken);
        var build = GeRosterSnapshotBuilder.Build(cue, schoolYear, students);
        if (build.StudentCount == 0 || build.Sections.Count == 0)
        {
            // Una respuesta vacía no es un padrón válido. No se consulta ni modifica
            // el snapshot anterior para evitar reemplazar datos útiles por un error GE.
            throw new GeRosterEmptyException(build.Cue, build.SchoolYear);
        }

        var existing = await rosterStore.FindByChecksumAsync(build.Cue, build.SchoolYear, build.Checksum, cancellationToken);
        if (existing is not null)
        {
            return ToRefreshResult(existing, created: false);
        }

        var fetchedAt = _timeProvider.GetUtcNow();
        var snapshot = new GeRosterSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            Cue = build.Cue,
            SchoolYear = build.SchoolYear,
            FetchedAt = fetchedAt,
            Checksum = build.Checksum,
            SectionCount = build.Sections.Count,
            StudentCount = build.StudentCount,
            Status = "Ready"
        };

        foreach (var sectionBuild in build.Sections)
        {
            var section = new GeRosterSection
            {
                Id = Guid.NewGuid().ToString("N"),
                SnapshotId = snapshot.Id,
                GeSectionId = sectionBuild.GeSectionId,
                Course = sectionBuild.Course,
                Division = sectionBuild.Division,
                Level = sectionBuild.Level,
                Shift = sectionBuild.Shift
            };
            snapshot.Sections.Add(section);

            foreach (var studentBuild in sectionBuild.Students)
            {
                section.Students.Add(new GeDomainRosterStudent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SnapshotId = snapshot.Id,
                    SectionId = section.Id,
                    GePersonId = studentBuild.GePersonId,
                    Document = studentBuild.Document,
                    FirstName = studentBuild.FirstName,
                    LastName = studentBuild.LastName
                });
            }
        }

        try
        {
            await rosterStore.AddAsync(snapshot, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dos refresh manuales pueden descargar el mismo padrón al mismo tiempo.
            // El índice único de checksum elige al ganador; el otro recupera ese snapshot.
            var concurrentSnapshot = await rosterStore.FindByChecksumAsync(build.Cue, build.SchoolYear, build.Checksum, cancellationToken);
            if (concurrentSnapshot is null)
            {
                throw;
            }

            return ToRefreshResult(concurrentSnapshot, created: false);
        }

        return ToRefreshResult(snapshot, created: true);
    }

    public Task<GeRosterSnapshot?> GetLatestAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var normalizedCue = NormalizeCue(cue);
        var normalizedSchoolYear = schoolYear?.Trim() ?? string.Empty;
        return rosterStore.GetLatestAsync(normalizedCue, normalizedSchoolYear, cancellationToken);
    }

    public async Task<IReadOnlyList<GeRosterSectionStatus>> GetSectionsAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestAsync(cue, schoolYear, cancellationToken);
        if (latest is null)
        {
            return [];
        }

        return await rosterStore.GetSectionsAsync(latest.Id, cancellationToken);
    }

    private static string NormalizeCue(string? cue) => CueCode.Normalize(cue);

    private static GeRosterRefreshResult ToRefreshResult(GeRosterSnapshot snapshot, bool created)
    {
        return new GeRosterRefreshResult(
            snapshot.Id,
            snapshot.Cue,
            snapshot.SchoolYear,
            snapshot.FetchedAt,
            snapshot.Checksum,
            snapshot.SectionCount,
            snapshot.StudentCount,
            snapshot.Status,
            created);
    }
}

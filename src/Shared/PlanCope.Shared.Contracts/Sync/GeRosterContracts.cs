using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Shared.Contracts.Sync;

public sealed record GeRosterPullRequest(string? Cue, string? SchoolYear);

public sealed record GeRosterPackageDto(
    string SnapshotId,
    string Cue,
    string SchoolYear,
    DateTimeOffset FetchedAt,
    string Checksum,
    int SectionCount,
    int StudentCount,
    string Status,
    IReadOnlyList<GeRosterSectionPackageDto> Sections,
    string? SchoolName = null);

public sealed record GeRosterSectionPackageDto(
    string Id,
    int? GeSectionId,
    string? Course,
    string? Division,
    string? Level,
    string? Shift,
    IReadOnlyList<GeRosterStudentPackageDto> Students);

public sealed record GeRosterStudentPackageDto(
    string Id,
    string SectionId,
    int GePersonId,
    string Document,
    string FirstName,
    string LastName);

public static class GeRosterTransportLimits
{
    public const int MaxCueLength = CueCode.Length;
    public const int MaxSchoolYearLength = 16;
    public const int MaxSections = 10_000;
    public const int MaxStudents = 1_000_000;
    public const int MaxFieldLength = 256;
}

public static class GeRosterPackageChecksum
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Calculate(GeRosterPackageDto package)
    {
        var rows = package.Sections
            .SelectMany(section => section.Students.Select(student => new CanonicalRosterRow(
                SectionKey(section, package.Cue),
                section.GeSectionId,
                NormalizeText(package.Cue).ToUpperInvariant(),
                NormalizeNullableText(section.Course),
                NormalizeNullableText(section.Division),
                NormalizeNullableText(section.Level),
                NormalizeNullableText(section.Shift),
                student.GePersonId,
                NormalizeDocument(student.Document),
                NormalizeText(student.FirstName),
                NormalizeText(student.LastName))))
            .Where(static row => row.GePersonId > 0)
            .GroupBy(static row => string.Join('|', row.SectionKey, row.GePersonId), StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static row => row.SectionKey, StringComparer.Ordinal)
            .ThenBy(static row => row.GePersonId)
            .ThenBy(static row => row.Document, StringComparer.Ordinal)
            .ThenBy(static row => row.LastName, StringComparer.Ordinal)
            .ThenBy(static row => row.FirstName, StringComparer.Ordinal)
            .ToList();

        var canonical = JsonSerializer.Serialize(rows, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string SectionKey(GeRosterSectionPackageDto section, string cue)
    {
        return section.GeSectionId.HasValue
            ? $"id:{section.GeSectionId.Value}"
            : string.Join('|', "fields", NormalizeText(cue).ToUpperInvariant(), NormalizeText(section.Course), NormalizeText(section.Division), NormalizeText(section.Level), NormalizeText(section.Shift));
    }

    private static string NormalizeDocument(string? value) => value is null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeNullableText(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

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

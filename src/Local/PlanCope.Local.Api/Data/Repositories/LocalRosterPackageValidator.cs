using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Local.Api.Data.Repositories;

/// <summary>
/// Validates the complete package before any local connection is opened. Keeping
/// this boundary strict makes the import safe to retry and prevents malformed
/// packages from partially populating the roster tables.
/// </summary>
public static class LocalRosterPackageValidator
{
    public static void Validate(GeRosterPackageDto package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.Sections is null ||
            string.IsNullOrWhiteSpace(package.SnapshotId) || package.SnapshotId.Length > 64 ||
            !CueCode.TryNormalize(package.Cue, out var normalizedCue) ||
            string.IsNullOrWhiteSpace(package.SchoolYear) || package.SchoolYear.Length > GeRosterTransportLimits.MaxSchoolYearLength ||
            !ValidOptionalField(package.SchoolName, GeRosterTransportLimits.MaxFieldLength) ||
            string.IsNullOrWhiteSpace(package.Status) || package.Status.Length > 32)
        {
            throw new ArgumentException("Roster package metadata is invalid.", nameof(package));
        }

        if (!string.Equals(package.Cue, normalizedCue, StringComparison.Ordinal))
        {
            throw new ArgumentException("Roster package CUE must use the canonical 9-digit format.", nameof(package));
        }

        if (package.SectionCount != package.Sections.Count ||
            package.SectionCount < 0 || package.SectionCount > GeRosterTransportLimits.MaxSections)
        {
            throw new ArgumentException("Roster section count is invalid.", nameof(package));
        }

        var students = package.Sections
            .Where(static section => section is not null)
            .SelectMany(static section => section.Students ?? [])
            .Where(static student => student is not null)
            .ToList();
        if (package.StudentCount != students.Count ||
            package.StudentCount < 0 || package.StudentCount > GeRosterTransportLimits.MaxStudents)
        {
            throw new ArgumentException("Roster student count is invalid.", nameof(package));
        }

        if (!IsSha256(package.Checksum) ||
            !string.Equals(package.Checksum, GeRosterPackageChecksum.Calculate(package), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Roster package checksum is invalid.", nameof(package));
        }

        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        var studentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in package.Sections)
        {
            if (section is null || string.IsNullOrWhiteSpace(section.Id) || section.Id.Length > 64 ||
                !sectionIds.Add(section.Id) ||
                !ValidOptionalField(section.Course, 64) || !ValidOptionalField(section.Division, 64) ||
                !ValidOptionalField(section.Level, 128) || !ValidOptionalField(section.Shift, 64))
            {
                throw new ArgumentException("Roster section is invalid.", nameof(package));
            }

            if (section.Students is null)
            {
                throw new ArgumentException("Roster section students are invalid.", nameof(package));
            }

            foreach (var student in section.Students)
            {
                if (student is null || string.IsNullOrWhiteSpace(student.Id) || student.Id.Length > 64 ||
                    !studentIds.Add(student.Id) || !string.Equals(student.SectionId, section.Id, StringComparison.Ordinal) ||
                    student.GePersonId <= 0 || !ValidRequiredField(student.FirstName, GeRosterTransportLimits.MaxFieldLength) ||
                    !ValidRequiredField(student.LastName, GeRosterTransportLimits.MaxFieldLength) ||
                    !ValidDocument(student.Document))
                {
                    throw new ArgumentException("Roster student is invalid.", nameof(package));
                }
            }
        }
    }

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(static character => Uri.IsHexDigit(character));

    private static bool ValidDocument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            return false;
        }

        var normalized = value.Where(char.IsDigit).ToArray();
        return normalized.Length > 0 && normalized.Length <= 32;
    }

    private static bool ValidRequiredField(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static bool ValidOptionalField(string? value, int maxLength) =>
        value is null || value.Length <= maxLength;
}

using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class GeRosterSnapshotBuilderTests
{
    [Fact]
    public void Build_IsDeterministicAndDeduplicatesIdenticalEnrollmentRows()
    {
        var first = new GeRosterStudent(7, 100, "1800554-00", "6º", "A", "Primario", "Mañana", "Pérez", "Ana", "12.345.678");
        var duplicate = new GeRosterStudent(7, 100, "1800554-00", "6º", "A", "Primario", "Mañana", "Pérez", "Ana", "12-345-678");

        var forward = GeRosterSnapshotBuilder.Build(" 1800554-00 ", "2026", [first, duplicate]);
        var reversed = GeRosterSnapshotBuilder.Build("1800554-00", "2026", [duplicate, first]);

        Assert.Equal(forward.Checksum, reversed.Checksum);
        Assert.Equal(1, forward.StudentCount);
        Assert.Equal("180055400", forward.Cue);
        Assert.Single(forward.Sections);
        Assert.Equal("12345678", forward.Sections[0].Students[0].Document);
    }

    [Fact]
    public void Build_PreservesSamePersonInDifferentSections()
    {
        var firstSection = new GeRosterStudent(7, 100, "1800554-00", "6º", "A", "Primario", "Mañana", "Pérez", "Ana", "12345678");
        var secondSection = new GeRosterStudent(7, 200, "1800554-00", "6º", "B", "Primario", "Mañana", "Pérez", "Ana", "12345678");

        var result = GeRosterSnapshotBuilder.Build("1800554-00", "2026", [firstSection, secondSection]);

        Assert.Equal(2, result.StudentCount);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal([100, 200], result.Sections.Select(section => section.GeSectionId).ToArray());
    }

    [Theory]
    [InlineData("", "2026")]
    [InlineData("1800554-00", "")]
    public void Build_RequiresCueAndSchoolYear(string cue, string schoolYear)
    {
        Assert.Throws<ArgumentException>(() => GeRosterSnapshotBuilder.Build(cue, schoolYear, []));
    }

    [Fact]
    public void TransportPackageChecksum_IsStableAcrossSectionAndStudentOrder()
    {
        var sections = new[]
        {
            new GeRosterSectionPackageDto("section-b", 200, "6º", "B", "Primario", "Mañana", new[]
            {
                new GeRosterStudentPackageDto("student-2", "section-b", 2, "12.345.679", "Ana", "Pérez")
            }),
            new GeRosterSectionPackageDto("section-a", 100, "6º", "A", "Primario", "Mañana", new[]
            {
                new GeRosterStudentPackageDto("student-1", "section-a", 1, "12-345-678", "Juan", "Pérez")
            })
        };
        var package = new GeRosterPackageDto("snapshot", "180055400", "2026", DateTimeOffset.UnixEpoch, "", 2, 2, "Ready", sections);
        var checksum = GeRosterPackageChecksum.Calculate(package);
        var reordered = new GeRosterPackageDto(
            package.SnapshotId,
            package.Cue,
            package.SchoolYear,
            package.FetchedAt,
            package.Checksum,
            package.SectionCount,
            package.StudentCount,
            package.Status,
            sections.OrderBy(static section => section.Id).ToArray());

        Assert.Equal(checksum, GeRosterPackageChecksum.Calculate(reordered));
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void BuildChecksum_MatchesExportedPackageWhenCueAnexoIsMissingOrFormatted()
    {
        var withoutCueAnexo = new GeRosterStudent(7, null, null, "6º", "A", "Primario", "Mañana", "Pérez", "Ana", "12.345.678");
        var spacedCueAnexo = withoutCueAnexo with { CueAnexo = " 1800554-00 " };
        var buildWithoutCue = GeRosterSnapshotBuilder.Build(" 1800554-00 ", "2026", [withoutCueAnexo]);
        var buildWithSpacedCue = GeRosterSnapshotBuilder.Build("1800554-00", "2026", [spacedCueAnexo]);
        var section = buildWithoutCue.Sections.Single();
        var packageWithoutChecksum = new GeRosterPackageDto(
            "snapshot",
            buildWithoutCue.Cue,
            buildWithoutCue.SchoolYear,
            DateTimeOffset.UnixEpoch,
            "",
            1,
            1,
            "Ready",
            [new GeRosterSectionPackageDto(
                "section-a",
                section.GeSectionId,
                section.Course,
                section.Division,
                section.Level,
                section.Shift,
                [new GeRosterStudentPackageDto(
                    "student-a",
                    "section-a",
                    section.Students[0].GePersonId,
                    section.Students[0].Document,
                    section.Students[0].FirstName,
                    section.Students[0].LastName)])]);

        Assert.Equal(buildWithoutCue.Checksum, buildWithSpacedCue.Checksum);
        Assert.Equal(buildWithoutCue.Checksum, GeRosterPackageChecksum.Calculate(packageWithoutChecksum));
    }
}

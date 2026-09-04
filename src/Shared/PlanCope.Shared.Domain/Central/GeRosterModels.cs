namespace PlanCope.Shared.Domain.Central;

public sealed class GeRosterSnapshot
{
    public string Id { get; set; } = string.Empty;

    public string Cue { get; set; } = string.Empty;

    public string SchoolYear { get; set; } = string.Empty;

    public DateTimeOffset FetchedAt { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public int SectionCount { get; set; }

    public int StudentCount { get; set; }

    public string Status { get; set; } = string.Empty;

    public ICollection<GeRosterSection> Sections { get; set; } = new List<GeRosterSection>();
}

public sealed class GeRosterSection
{
    public string Id { get; set; } = string.Empty;

    public string SnapshotId { get; set; } = string.Empty;

    public int? GeSectionId { get; set; }

    public string? Course { get; set; }

    public string? Division { get; set; }

    public string? Level { get; set; }

    public string? Shift { get; set; }

    public GeRosterSnapshot Snapshot { get; set; } = null!;

    public ICollection<GeRosterStudent> Students { get; set; } = new List<GeRosterStudent>();
}

public sealed class GeRosterStudent
{
    public string Id { get; set; } = string.Empty;

    public string SnapshotId { get; set; } = string.Empty;

    public string SectionId { get; set; } = string.Empty;

    public int GePersonId { get; set; }

    public string Document { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public GeRosterSnapshot Snapshot { get; set; } = null!;

    public GeRosterSection Section { get; set; } = null!;
}

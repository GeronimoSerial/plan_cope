using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class GeRosterSnapshotConfiguration : IEntityTypeConfiguration<GeRosterSnapshot>
{
    public void Configure(EntityTypeBuilder<GeRosterSnapshot> builder)
    {
        builder.ToTable("roster_snapshots", "roster");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Cue).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.SchoolYear).HasMaxLength(16).IsRequired();
        builder.Property(static x => x.Checksum).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => new { x.Cue, x.SchoolYear, x.Checksum }).IsUnique();
        builder.HasIndex(static x => new { x.Cue, x.SchoolYear, x.FetchedAt });
        builder.HasMany(static x => x.Sections)
            .WithOne(static x => x.Snapshot)
            .HasForeignKey(static x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GeRosterSectionConfiguration : IEntityTypeConfiguration<GeRosterSection>
{
    public void Configure(EntityTypeBuilder<GeRosterSection> builder)
    {
        builder.ToTable("roster_sections", "roster");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.SnapshotId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Course).HasMaxLength(64);
        builder.Property(static x => x.Division).HasMaxLength(64);
        builder.Property(static x => x.Level).HasMaxLength(128);
        builder.Property(static x => x.Shift).HasMaxLength(64);
        builder.HasIndex(static x => x.SnapshotId);
        builder.HasIndex(static x => new
        {
            x.SnapshotId,
            x.GeSectionId,
            x.Course,
            x.Division,
            x.Level,
            x.Shift
        }).IsUnique();
        builder.HasMany(static x => x.Students)
            .WithOne(static x => x.Section)
            .HasForeignKey(static x => x.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GeRosterStudentConfiguration : IEntityTypeConfiguration<GeRosterStudent>
{
    public void Configure(EntityTypeBuilder<GeRosterStudent> builder)
    {
        builder.ToTable("roster_students", "roster");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.SnapshotId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.SectionId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Document).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.FirstName).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.LastName).HasMaxLength(256).IsRequired();
        builder.HasIndex(static x => new { x.SnapshotId, x.Document });
        builder.HasIndex(static x => new { x.SnapshotId, x.SectionId, x.GePersonId }).IsUnique();
        builder.HasOne(static x => x.Snapshot)
            .WithMany()
            .HasForeignKey(static x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

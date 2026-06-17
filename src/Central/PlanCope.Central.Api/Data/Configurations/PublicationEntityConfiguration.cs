using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class PublicationPackageConfiguration : IEntityTypeConfiguration<PublicationPackage>
{
    public void Configure(EntityTypeBuilder<PublicationPackage> builder)
    {
        builder.ToTable("packages", "publication");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ExamVersionId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Checksum).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Manifest).HasColumnType("jsonb").IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => new { x.ExamVersionId, x.PackageVersion }).IsUnique();
    }
}

public sealed class PublicationTargetConfiguration : IEntityTypeConfiguration<PublicationTarget>
{
    public void Configure(EntityTypeBuilder<PublicationTarget> builder)
    {
        builder.ToTable("targets", "publication");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.PublicationPackageId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.TargetId).HasMaxLength(64);
        builder.HasIndex(static x => new { x.PublicationPackageId, x.TargetType, x.TargetId });
    }
}

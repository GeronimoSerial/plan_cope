using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class ExamRollupConfiguration : IEntityTypeConfiguration<ExamRollup>
{
    public void Configure(EntityTypeBuilder<ExamRollup> builder)
    {
        builder.ToTable("exam_rollups", "stats");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Cue).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.SchoolYear).HasMaxLength(16).IsRequired();
        builder.Property(static x => x.Course).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ExamVersionId).HasMaxLength(64).IsRequired();
        builder.HasIndex(static x => new { x.Cue, x.SchoolYear, x.Course, x.ExamVersionId }).IsUnique();
    }
}

public sealed class ExamRollupBlockConfiguration : IEntityTypeConfiguration<ExamRollupBlock>
{
    public void Configure(EntityTypeBuilder<ExamRollupBlock> builder)
    {
        builder.ToTable("exam_rollup_blocks", "stats");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.RollupId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.BlockId).HasMaxLength(64).IsRequired();
        builder.HasIndex(static x => new { x.RollupId, x.BlockId }).IsUnique();
        builder.HasOne<ExamRollup>()
            .WithMany()
            .HasForeignKey(static x => x.RollupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

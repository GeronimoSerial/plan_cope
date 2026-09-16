using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class CentralAttemptResultConfiguration : IEntityTypeConfiguration<CentralAttemptResult>
{
    public void Configure(EntityTypeBuilder<CentralAttemptResult> builder)
    {
        builder.ToTable("attempt_results", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ReceivedStudentAttemptId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ScoringPolicy).HasMaxLength(64);
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.BlocksJson).HasColumnType("jsonb");
        builder.HasIndex(static x => new { x.ReceivedStudentAttemptId, x.GradingSchemaVersion }).IsUnique();
    }
}
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class GradingPolicyAssignmentConfiguration : IEntityTypeConfiguration<GradingPolicyAssignment>
{
    public void Configure(EntityTypeBuilder<GradingPolicyAssignment> builder)
    {
        builder.ToTable("grading_policies", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ExamVersionId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.ScoringPolicy).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.AssignedBy).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.AssignedAt).IsRequired();
        builder.Property(static x => x.Note).HasMaxLength(512);
        builder.HasIndex(static x => x.ExamVersionId).IsUnique();
    }
}

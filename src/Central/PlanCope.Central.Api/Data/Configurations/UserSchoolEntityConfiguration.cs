using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class UserSchoolAssignmentConfiguration : IEntityTypeConfiguration<UserSchoolAssignment>
{
    public void Configure(EntityTypeBuilder<UserSchoolAssignment> builder)
    {
        builder.ToTable("user_schools", "core");
        builder.HasKey(static x => new { x.UserId, x.Cue });
        builder.Property(static x => x.UserId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Cue).HasMaxLength(9).IsRequired();
        builder.HasIndex(static x => x.UserId);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(static x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
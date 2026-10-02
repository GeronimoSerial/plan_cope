using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class StatsShareConfiguration : IEntityTypeConfiguration<StatsShare>
{
    public void Configure(EntityTypeBuilder<StatsShare> builder)
    {
        builder.ToTable("stats_shares", "stats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.Property(x => x.GroupBy).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Filters).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ExpiresAt, x.RevokedAt });
    }
}

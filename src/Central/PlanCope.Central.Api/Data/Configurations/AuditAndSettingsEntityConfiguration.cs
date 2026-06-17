using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("logs", "audit");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.EntityId).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Payload).HasColumnType("jsonb");
        builder.HasIndex(static x => x.CreatedAt);
    }
}

public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("settings", "settings");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Value).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(static x => x.Key).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(static x => x.FullName).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => x.Email).IsUnique();
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(static x => x.Code).IsUnique();
    }
}

public sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_roles", "core");
        builder.HasKey(static x => new { x.UserId, x.RoleId });
        builder.Property(static x => x.UserId).HasColumnType("uuid");
        builder.Property(static x => x.RoleId).HasColumnType("uuid");
    }
}

public sealed class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("provinces", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.Name).HasMaxLength(128).IsRequired();
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ProvinceId).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(static x => x.ProvinceId);
    }
}

public sealed class LocalityConfiguration : IEntityTypeConfiguration<Locality>
{
    public void Configure(EntityTypeBuilder<Locality> builder)
    {
        builder.ToTable("localities", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.DepartmentId).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.PostalCode).HasMaxLength(32);
        builder.Property(static x => x.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(static x => x.DepartmentId);
    }
}

public sealed class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    public void Configure(EntityTypeBuilder<School> builder)
    {
        builder.ToTable("schools", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.LocalityId).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => x.Cue);
        builder.HasIndex(static x => x.LocalityId);
    }
}

public sealed class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> builder)
    {
        builder.ToTable("classrooms", "core");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.SchoolId).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Shift).HasMaxLength(64);
        builder.Property(static x => x.Metadata).HasColumnType("jsonb");
        builder.HasIndex(static x => x.SchoolId);
    }
}

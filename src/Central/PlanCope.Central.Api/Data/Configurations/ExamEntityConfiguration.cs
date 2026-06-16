using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable("exams", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => x.Code).IsUnique();
    }
}

public sealed class ExamVersionConfiguration : IEntityTypeConfiguration<ExamVersion>
{
    public void Configure(EntityTypeBuilder<ExamVersion> builder)
    {
        builder.ToTable("versions", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamId).HasColumnType("uuid");
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.Metadata).HasColumnType("jsonb");
        builder.HasIndex(static x => new { x.ExamId, x.VersionNumber }).IsUnique();
    }
}

public sealed class ExamBlockConfiguration : IEntityTypeConfiguration<ExamBlock>
{
    public void Configure(EntityTypeBuilder<ExamBlock> builder)
    {
        builder.ToTable("blocks", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamVersionId).HasColumnType("uuid");
        builder.Property(static x => x.BlockType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Config).HasColumnType("jsonb").IsRequired();
        builder.Property(static x => x.Validation).HasColumnType("jsonb");
        builder.HasIndex(static x => new { x.ExamVersionId, x.OrderIndex });
    }
}

public sealed class ExamBlockOptionConfiguration : IEntityTypeConfiguration<ExamBlockOption>
{
    public void Configure(EntityTypeBuilder<ExamBlockOption> builder)
    {
        builder.ToTable("block_options", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamBlockId).HasColumnType("uuid");
        builder.Property(static x => x.Value).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Label).HasMaxLength(512).IsRequired();
        builder.Property(static x => x.Metadata).HasColumnType("jsonb");
        builder.HasIndex(static x => new { x.ExamBlockId, x.OrderIndex });
    }
}

public sealed class AnswerKeyConfiguration : IEntityTypeConfiguration<AnswerKey>
{
    public void Configure(EntityTypeBuilder<AnswerKey> builder)
    {
        builder.ToTable("answer_keys", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamBlockId).HasColumnType("uuid");
        builder.Property(static x => x.CorrectAnswer).HasColumnType("jsonb").IsRequired();
        builder.Property(static x => x.Metadata).HasColumnType("jsonb");
        builder.HasIndex(static x => x.ExamBlockId).IsUnique();
    }
}

public sealed class ExamAssetConfiguration : IEntityTypeConfiguration<ExamAsset>
{
    public void Configure(EntityTypeBuilder<ExamAsset> builder)
    {
        builder.ToTable("assets", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamVersionId).HasColumnType("uuid");
        builder.Property(static x => x.FileName).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.MimeType).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Checksum).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.StoragePath).HasMaxLength(512).IsRequired();
        builder.HasIndex(static x => x.ExamVersionId);
        builder.HasIndex(static x => x.Checksum);
    }
}

public sealed class AssetUsageConfiguration : IEntityTypeConfiguration<AssetUsage>
{
    public void Configure(EntityTypeBuilder<AssetUsage> builder)
    {
        builder.ToTable("asset_usages", "exam");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasColumnType("uuid");
        builder.Property(static x => x.ExamBlockId).HasColumnType("uuid");
        builder.Property(static x => x.ExamAssetId).HasColumnType("uuid");
        builder.Property(static x => x.UsageType).HasMaxLength(64).IsRequired();
        builder.HasIndex(static x => x.ExamBlockId);
        builder.HasIndex(static x => x.ExamAssetId);
    }
}

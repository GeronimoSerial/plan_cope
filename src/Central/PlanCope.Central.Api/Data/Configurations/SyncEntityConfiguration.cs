using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data.Configurations;

public sealed class RegisteredNodeConfiguration : IEntityTypeConfiguration<RegisteredNode>
{
    public void Configure(EntityTypeBuilder<RegisteredNode> builder)
    {
        builder.ToTable("registered_nodes", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.SchoolId).HasMaxLength(64);
        builder.Property(static x => x.NodeCode).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.DeviceName).HasMaxLength(256);
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.FingerprintHash).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.FingerprintComponents).HasColumnType("jsonb").IsRequired();
        builder.Property(static x => x.Cue).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.ActivationKeyId).HasMaxLength(64);
        builder.Property(static x => x.AppVersion).HasMaxLength(64);
        builder.HasIndex(static x => x.NodeCode).IsUnique();
        builder.HasIndex(static x => x.Cue);
    }
}

public sealed class ActivationKeyConfiguration : IEntityTypeConfiguration<ActivationKey>
{
    public void Configure(EntityTypeBuilder<ActivationKey> builder)
    {
        builder.ToTable("activation_keys", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.KeyHash).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.KeyPrefix).HasMaxLength(8).IsRequired();
        builder.Property(static x => x.IssuedBy).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.RevokedReason).HasMaxLength(256);
        builder.Property(static x => x.ScopeCue).HasMaxLength(32);
        builder.Property(static x => x.Note).HasMaxLength(512);
        builder.HasIndex(static x => x.KeyHash).IsUnique();
        builder.HasIndex(static x => x.KeyPrefix);
        builder.HasIndex(static x => x.ScopeCue);
    }
}

public sealed class NodeCredentialConfiguration : IEntityTypeConfiguration<NodeCredential>
{
    public void Configure(EntityTypeBuilder<NodeCredential> builder)
    {
        builder.ToTable("node_credentials", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.NodeId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.RefreshTokenHash).HasMaxLength(256).IsRequired();
        builder.Property(static x => x.RotatedFrom).HasMaxLength(64);
        builder.HasIndex(static x => x.RefreshTokenHash).IsUnique();
        builder.HasIndex(static x => x.NodeId);
        builder.HasOne<NodeCredential>()
            .WithMany()
            .HasForeignKey(static x => x.RotatedFrom)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CentralDeliverySessionConfiguration : IEntityTypeConfiguration<CentralDeliverySession>
{
    public void Configure(EntityTypeBuilder<CentralDeliverySession> builder)
    {
        builder.ToTable("delivery_sessions", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.RemoteLocalId).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => x.RemoteLocalId).IsUnique();
    }
}

public sealed class ReceivedStudentAttemptConfiguration : IEntityTypeConfiguration<ReceivedStudentAttempt>
{
    public void Configure(EntityTypeBuilder<ReceivedStudentAttempt> builder)
    {
        builder.ToTable("received_student_attempts", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.RemoteLocalId).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.StudentCode).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.RosterStudentId).HasMaxLength(128);
        builder.Property(static x => x.RosterSnapshotId).HasMaxLength(128);
        builder.Property(static x => x.RosterSectionId).HasMaxLength(128);
        builder.Property(static x => x.StudentFirstName).HasMaxLength(256);
        builder.Property(static x => x.StudentLastName).HasMaxLength(256);
        builder.Property(static x => x.DocumentLast4).HasMaxLength(4);
        builder.Property(static x => x.VerificationSource).HasMaxLength(64);
        builder.HasIndex(static x => x.IdempotencyKey).IsUnique();
    }
}

public sealed class ReceivedSubmissionAnswerConfiguration : IEntityTypeConfiguration<ReceivedSubmissionAnswer>
{
    public void Configure(EntityTypeBuilder<ReceivedSubmissionAnswer> builder)
    {
        builder.ToTable("received_submission_answers", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.StudentAttemptId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.BlockId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.Answer).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(static x => x.StudentAttemptId);
    }
}

public sealed class SyncInboxConfiguration : IEntityTypeConfiguration<SyncInbox>
{
    public void Configure(EntityTypeBuilder<SyncInbox> builder)
    {
        builder.ToTable("inbox", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.SourceNodeId).HasMaxLength(64);
        builder.Property(static x => x.EventType).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.AggregateType).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.AggregateId).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(static x => x.IdempotencyKey).IsUnique();
    }
}

public sealed class SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor>
{
    public void Configure(EntityTypeBuilder<SyncCursor> builder)
    {
        builder.ToTable("cursors", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.NodeId).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.CursorKey).HasMaxLength(128).IsRequired();
        builder.Property(static x => x.CursorValue).HasMaxLength(128).IsRequired();
        builder.HasIndex(static x => new { x.NodeId, x.CursorKey }).IsUnique();
    }
}

public sealed class SyncAttemptConfiguration : IEntityTypeConfiguration<SyncAttempt>
{
    public void Configure(EntityTypeBuilder<SyncAttempt> builder)
    {
        builder.ToTable("sync_attempts", "sync");
        builder.HasKey(static x => x.Id);
        builder.Property(static x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(static x => x.NodeId).HasMaxLength(64);
        builder.Property(static x => x.Direction).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(static x => x.Summary).HasColumnType("jsonb");
        builder.Property(static x => x.Error).HasColumnType("jsonb");
        builder.HasIndex(static x => x.StartedAt);
    }
}

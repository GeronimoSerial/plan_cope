using Microsoft.EntityFrameworkCore;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data;

public sealed class PlanCopeDbContext(DbContextOptions<PlanCopeDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Locality> Localities => Set<Locality>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamVersion> ExamVersions => Set<ExamVersion>();
    public DbSet<ExamBlock> ExamBlocks => Set<ExamBlock>();
    public DbSet<ExamBlockOption> ExamBlockOptions => Set<ExamBlockOption>();
    public DbSet<AnswerKey> AnswerKeys => Set<AnswerKey>();
    public DbSet<ExamAsset> ExamAssets => Set<ExamAsset>();
    public DbSet<AssetUsage> AssetUsages => Set<AssetUsage>();
    public DbSet<PublicationPackage> PublicationPackages => Set<PublicationPackage>();
    public DbSet<PublicationTarget> PublicationTargets => Set<PublicationTarget>();
    public DbSet<RegisteredNode> RegisteredNodes => Set<RegisteredNode>();
    public DbSet<CentralDeliverySession> DeliverySessions => Set<CentralDeliverySession>();
    public DbSet<ReceivedStudentAttempt> ReceivedStudentAttempts => Set<ReceivedStudentAttempt>();
    public DbSet<ReceivedSubmissionAnswer> ReceivedSubmissionAnswers => Set<ReceivedSubmissionAnswer>();
    public DbSet<SyncInbox> SyncInbox => Set<SyncInbox>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<SyncAttempt> SyncAttempts => Set<SyncAttempt>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlanCopeDbContext).Assembly);
    }
}

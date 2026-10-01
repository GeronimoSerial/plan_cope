using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed record SessionListItem(
    string Id, string ExamVersionId, string SchoolCode, string SchoolName, string ExamTitle,
    string? GradeLabel, string StartAt, string? EndAt, string Status, string AccessCode,
    int ExpectedStudentCount, int SubmittedCount, int InProgressCount,
    string? RosterSnapshotId, string? RosterSectionId,
    int OffRosterSubmittedCount = 0, int OffRosterInProgressCount = 0);

public sealed record SessionHistoryPage(IReadOnlyList<SessionListItem> Items, int Page, int PageSize, int TotalCount);

public sealed record LocalSchoolListItem(string Code, string Name, bool HasReadyRoster);
public sealed record LocalSchoolWithAttempts(string Code, string Name, long SubmittedAttemptCount, string LastSubmittedAt);

public interface ISessionRepository
{
    Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default);

    Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalDeliverySession?> GetByIdOrAccessCodeAsync(string idOrAccessCode, CancellationToken cancellationToken = default);

    Task<bool> AccessCodeExistsAsync(string accessCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalDeliverySession>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionListItem>> GetActiveSummariesAsync(string? schoolCode, CancellationToken cancellationToken = default);

    Task<SessionHistoryPage> GetHistoryAsync(string? schoolCode, string? status, string? query, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSchoolListItem>> GetSchoolsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSchoolWithAttempts>> GetSchoolsWithAttemptsAsync(CancellationToken cancellationToken = default);

    Task<LocalSessionProgress?> GetProgressAsync(string idOrAccessCode, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(string id, string status, string? endAt = null, CancellationToken cancellationToken = default);

    Task<bool> TryCloseAsync(string id, string endAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetInProgressAttemptIdsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> DeleteIfNoAttemptsAsync(string id, CancellationToken cancellationToken = default);
}

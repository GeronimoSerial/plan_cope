using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface IAttemptRepository
{
    Task CreateResolutionAsync(StudentResolution resolution, CancellationToken cancellationToken = default);

    Task<NominalAttemptStartResult> StartNominalAttemptAsync(
        string deliverySessionId,
        string tokenHash,
        string startedAt,
        CancellationToken cancellationToken = default);

    Task CreateAsync(StudentAttempt attempt, CancellationToken cancellationToken = default);

    Task<StudentAttempt?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForStudentAsync(string deliverySessionId, string studentCode, CancellationToken cancellationToken = default);

    Task<int> GetNextLocalSequenceAsync(string deliverySessionId, CancellationToken cancellationToken = default);

    Task UpsertAnswersAsync(string attemptId, IReadOnlyList<SubmissionAnswer> answers, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionAnswer>> GetAnswersAsync(string attemptId, CancellationToken cancellationToken = default);

    Task SubmitAsync(string id, string submittedAt, string confirmationCode, CancellationToken cancellationToken = default);

    Task<bool> SubmitWithOutboxAsync(
        string id,
        string submittedAt,
        string confirmationCode,
        SyncOutbox outbox,
        CancellationToken cancellationToken = default);
}

public sealed record StudentResolution(
    string Id,
    string DeliverySessionId,
    string RosterSnapshotId,
    string RosterSectionId,
    string RosterStudentId,
    int GePersonId,
    string FirstName,
    string LastName,
    string DocumentLast4,
    string TokenHash,
    string ExpiresAt,
    string CreatedAt);

public enum NominalAttemptStartStatus
{
    Started,
    ResolutionNotFound,
    ResolutionExpired,
    ResolutionUsed,
    AttemptExists
}

public sealed record NominalAttemptStartResult(NominalAttemptStartStatus Status, StudentAttempt? Attempt)
{
    public static NominalAttemptStartResult Started(StudentAttempt attempt) => new(NominalAttemptStartStatus.Started, attempt);
}

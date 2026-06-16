using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface IAttemptRepository
{
    Task CreateAsync(StudentAttempt attempt, CancellationToken cancellationToken = default);

    Task<StudentAttempt?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForStudentAsync(string deliverySessionId, string studentCode, CancellationToken cancellationToken = default);

    Task<int> GetNextLocalSequenceAsync(string deliverySessionId, CancellationToken cancellationToken = default);

    Task UpsertAnswersAsync(string attemptId, IReadOnlyList<SubmissionAnswer> answers, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionAnswer>> GetAnswersAsync(string attemptId, CancellationToken cancellationToken = default);

    Task SubmitAsync(string id, string submittedAt, string confirmationCode, CancellationToken cancellationToken = default);
}

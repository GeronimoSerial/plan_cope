using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ISessionRepository
{
    Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default);

    Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalDeliverySession?> GetByIdOrAccessCodeAsync(string idOrAccessCode, CancellationToken cancellationToken = default);

    Task<bool> AccessCodeExistsAsync(string accessCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalDeliverySession>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<LocalSessionProgress?> GetProgressAsync(string idOrAccessCode, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(string id, string status, string? endAt = null, CancellationToken cancellationToken = default);

    Task<bool> TryCloseAsync(string id, string endAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetInProgressAttemptIdsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> DeleteIfNoAttemptsAsync(string id, CancellationToken cancellationToken = default);
}

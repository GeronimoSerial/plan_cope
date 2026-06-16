using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ISessionRepository
{
    Task CreateAsync(LocalDeliverySession session, CancellationToken cancellationToken = default);

    Task<LocalDeliverySession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalDeliverySession>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(string id, string status, string? endAt = null, CancellationToken cancellationToken = default);
}

using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ISyncStateRepository
{
    Task<SyncState?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task UpsertAsync(SyncState state, CancellationToken cancellationToken = default);
}

using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface INodeIdentityRepository
{
    Task<NodeIdentity?> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(NodeIdentity identity, CancellationToken cancellationToken = default);
}
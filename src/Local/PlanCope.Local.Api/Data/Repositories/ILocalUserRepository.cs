using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ILocalUserRepository
{
    Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<LocalUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}

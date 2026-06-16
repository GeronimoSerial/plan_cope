using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface ILocalExamRepository
{
    Task<IReadOnlyList<LocalExamVersion>> GetExamsAsync(string? grade = null, CancellationToken cancellationToken = default);

    Task<LocalExamVersion?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalExamBlock>> GetBlocksAsync(string localExamVersionId, CancellationToken cancellationToken = default);
}

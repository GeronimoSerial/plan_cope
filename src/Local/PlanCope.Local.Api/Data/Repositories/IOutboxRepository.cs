using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public interface IOutboxRepository
{
    Task InsertAsync(SyncOutbox item, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncOutbox>> GetPendingBatchAsync(int limit, string now, CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    Task MarkSentAsync(string id, string processedAt, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken = default);

    Task RequeueAsync(string id, int retryCount, string nextRetryAt, string? lastError, CancellationToken cancellationToken = default);
}

using System.Threading;
using System.Threading.Tasks;

namespace PlanCope.Local.Api.Data.Repositories;

public interface IStatsRollupRepository
{
    Task UpsertForAttemptAsync(string studentAttemptId, CancellationToken cancellationToken = default);
}
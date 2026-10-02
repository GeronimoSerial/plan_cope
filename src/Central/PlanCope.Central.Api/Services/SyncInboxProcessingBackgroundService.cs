using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Recovers durable inbox receipts whose downstream attempt processing did not finish. The
/// existing sync inbox is the work source; no parallel queue or response data is introduced.
/// </summary>
public sealed class SyncInboxProcessingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncInboxProcessingBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
                var now = DateTimeOffset.UtcNow;
                var pendingIds = await dbContext.SyncInbox.AsNoTracking()
                    .Where(item => item.Status == "received" || item.Status == "processing_failed")
                    .Where(item => item.NextProcessingAt == null || item.NextProcessingAt <= now)
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id)
                    .Select(item => item.Id)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                if (pendingIds.Count > 0)
                {
                    var controller = new SyncController(
                        dbContext,
                        scope.ServiceProvider.GetRequiredService<CentralStatsRollupService>(),
                        scope.ServiceProvider.GetRequiredService<ILogger<SyncController>>(),
                        scope.ServiceProvider.GetRequiredService<CentralAttemptGradingService>());
                    foreach (var inboxId in pendingIds)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        await controller.ProcessInboxByIdAsync(inboxId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Sync inbox recovery tick failed with {ErrorType}; the next tick will retry.", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

using PlanCope.Central.Api.Integrations.Ge;

namespace PlanCope.Central.Api.Services;

public sealed class RosterSyncBackgroundService(
    RosterSyncCoordinator coordinator,
    IConfiguration configuration,
    ILogger<RosterSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!coordinator.IsEnabled)
        {
            logger.LogWarning("Daily roster sync is disabled because Asistencias is not the active source or ConnectionStrings:Asistencias is not configured.");
            return;
        }

        try
        {
            if (await coordinator.SyncMissingAsync(stoppingToken) is { Enabled: true } initialResult)
            {
                logger.LogInformation("Startup catch-up roster sync processed {Count} CUEs with no current-year snapshot.", initialResult.TotalSchools);
                if (initialResult.Failed > 0 || initialResult.Empty > 0)
                {
                    logger.LogWarning("Startup catch-up left {Failed} failed and {Empty} empty CUEs; retrying only schools still without a current-year snapshot.",
                        initialResult.Failed, initialResult.Empty);
                    await coordinator.SyncMissingAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            logger.LogError(exception, "The startup roster sync failed before processing schools.");
        }

        var timeZone = AsistenciasRosterSource.ResolveTimeZone(configuration);
        var syncTime = TimeOnly.TryParse(configuration["Rosters:DailySyncTime"]?.Trim(), out var configuredTime)
            ? configuredTime
            : new TimeOnly(4, 30);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = UntilNextRun(DateTimeOffset.UtcNow, timeZone, syncTime);
            logger.LogInformation("Next all-school roster sync is scheduled in {Delay}.", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
                var result = await coordinator.SyncAllAsync(stoppingToken);
                if (result.Failed > 0 || result.Empty > 0)
                    await coordinator.SyncMissingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled all-school roster sync failed; the next daily run remains scheduled.");
            }
        }
    }

    internal static TimeSpan UntilNextRun(DateTimeOffset utcNow, TimeZoneInfo timeZone, TimeOnly syncTime)
    {
        var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var nextLocal = localNow.Date.Add(syncTime.ToTimeSpan());
        if (nextLocal <= localNow.DateTime) nextLocal = nextLocal.AddDays(1);
        while (timeZone.IsInvalidTime(nextLocal)) nextLocal = nextLocal.AddMinutes(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), timeZone);
        return nextUtc <= utcNow.UtcDateTime ? TimeSpan.Zero : nextUtc - utcNow.UtcDateTime;
    }
}

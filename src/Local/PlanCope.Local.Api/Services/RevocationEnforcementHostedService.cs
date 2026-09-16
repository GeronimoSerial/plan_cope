namespace PlanCope.Local.Api.Services;

/// <summary>
/// Autonomously advances revocation enforcement so a revoked node eventually drains its
/// outbox, wipes its roster cache and credential, and locks itself without operator action.
/// This is deliberately separate from <see cref="LocalOutboxPushService"/>, whose
/// operator/release-only push exists to control routine sync bandwidth and cost; security
/// enforcement must not depend on an operator or a release being present.
/// </summary>
public sealed class RevocationEnforcementHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<RevocationEnforcementHostedService> logger) : BackgroundService
{
    // Fixed interval, not configurable: this is a low-resource school machine and revocation
    // is not a latency-sensitive path, so a delay of up to ~30s to notice a closed session is
    // acceptable and cheaper than adding configuration surface for a single reasonable value.
    private static readonly TimeSpan RevocationCheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var enforcer = scope.ServiceProvider.GetRequiredService<RevocationEnforcer>();
                var outcome = await enforcer.TryAdvanceAsync(stoppingToken);
                if (outcome != RevocationEnforcementOutcome.NotApplicable)
                {
                    logger.LogDebug(
                        "Revocation enforcement advanced with outcome {Outcome}.",
                        outcome);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Revocation enforcement tick failed; the next tick will run after the fixed interval.");
            }

            try
            {
                await Task.Delay(RevocationCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

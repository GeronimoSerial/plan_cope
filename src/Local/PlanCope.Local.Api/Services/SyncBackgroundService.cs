using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Services;

/// <summary>
/// Autonomously syncs exams and the outbox so the node stays current without an operator
/// pressing anything. The manual /api/sync endpoints remain for diagnostics and manual
/// override; this service is the background equivalent. It never syncs while a delivery
/// session is active or paused (a classroom mid-exam), and it probes Central with a single
/// cheap unauthenticated request before every pull/push so a dead school network is detected
/// fast and retried with full-jitter exponential backoff instead of hammering the endpoint.
/// Roster pulls stay manual-only by design: they need a CUE and school year this service has
/// no reliable source for.
/// </summary>
public sealed class SyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<SyncBackgroundService> logger) : BackgroundService
{
    // Fixed constants, not configurable: this is a low-resource school machine and sync is not
    // a latency-sensitive path, so a fixed idle interval and a fixed exam-gate recheck are
    // cheaper than adding configuration surface for single reasonable values.
    private static readonly TimeSpan ExamSessionRecheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);
    private const int BackoffBaseSeconds = 5;
    private const int BackoffCapSeconds = 300;
    private const int BackoffMaxExponent = 6;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // Consecutive probe-failure counter; reset to 0 on any probe success.
    private int attempt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = IdleInterval;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
                var syncStateRepository = scope.ServiceProvider.GetRequiredService<ISyncStateRepository>();
                var examPullService = scope.ServiceProvider.GetRequiredService<LocalExamPullService>();
                var outboxPushService = scope.ServiceProvider.GetRequiredService<LocalOutboxPushService>();

                // Hard gate: never sync while a delivery session is active or paused. Delivery
                // latency for a student mid-exam beats sync freshness, and this check runs before
                // any network call on every single tick.
                var activeSessions = await sessionRepository.GetActiveAsync(stoppingToken);
                if (activeSessions.Count > 0)
                {
                    nextDelay = ExamSessionRecheckInterval;
                }
                else
                {
                    var centralUrl = await ReadStateStringAsync(syncStateRepository, "central_url", stoppingToken);
                    var probe = await ProbeCentralAsync(centralUrl, stoppingToken);
                    if (!probe.Success)
                    {
                        attempt++;
                        var cappedExponent = Math.Min(attempt, BackoffMaxExponent);
                        var maxDelay = Math.Min(BackoffCapSeconds, BackoffBaseSeconds * Math.Pow(2, cappedExponent));
                        var delaySeconds = Random.Shared.NextDouble() * maxDelay;

                        await UpsertStateAsync(syncStateRepository, "sync_offline",
                            JsonSerializer.Serialize(true, JsonOptions),
                            stoppingToken);
                        await UpsertStateAsync(syncStateRepository, "sync_next_attempt_at",
                            JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddSeconds(delaySeconds), JsonOptions),
                            stoppingToken);
                        nextDelay = TimeSpan.FromSeconds(delaySeconds);
                    }
                    else
                    {
                        attempt = 0;

                        await UpsertStateAsync(syncStateRepository, "sync_offline",
                            JsonSerializer.Serialize(false, JsonOptions),
                            stoppingToken);

                        var pull = await examPullService.PullAsync(stoppingToken);
                        if (!pull.Success)
                        {
                            await UpsertStateAsync(syncStateRepository, "sync_last_error",
                                JsonSerializer.Serialize(pull.Error ?? "exam pull failed.", JsonOptions),
                                stoppingToken);
                        }

                        var push = await outboxPushService.PushAsync(200, stoppingToken);
                        if (!push.Success)
                        {
                            await UpsertStateAsync(syncStateRepository, "sync_last_error",
                                JsonSerializer.Serialize(push.Error ?? "outbox push failed.", JsonOptions),
                                stoppingToken);
                        }

                        // Only clears the operator-visible error when both pull and push succeeded.
                        if (pull.Success && push.Success)
                        {
                            await UpsertStateAsync(syncStateRepository, "sync_last_error",
                                JsonSerializer.Serialize("", JsonOptions),
                                stoppingToken);
                        }

                        await UpsertStateAsync(syncStateRepository, "sync_next_attempt_at",
                            JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddSeconds(IdleInterval.TotalSeconds), JsonOptions),
                            stoppingToken);
                        nextDelay = IdleInterval;
                    }
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
                    "Autonomous sync tick failed; the next tick will run after the fixed interval.");
            }

            try
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Single unauthenticated GET to {central_url}/health/live. Deliberately not retried: this is
    /// a fast connectivity check, not a resilience-sensitive call.
    /// </summary>
    private async Task<(bool Success, string? Reason)> ProbeCentralAsync(string? centralUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(centralUrl) ||
            !Uri.TryCreate(centralUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("http" or "https"))
        {
            return (false, "central_url is not configured");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(SyncBackgroundService));
            client.BaseAddress = baseAddress;
            using var response = await client.GetAsync("health/live", cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"health/live returned {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (false, exception.Message);
        }
    }

    private static async Task<string?> ReadStateStringAsync(ISyncStateRepository syncStateRepository, string key, CancellationToken cancellationToken)
    {
        var state = await syncStateRepository.GetAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(state?.ValueJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(state.ValueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }

    private static Task UpsertStateAsync(ISyncStateRepository syncStateRepository, string key, string valueJson, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return syncStateRepository.UpsertAsync(new SyncState(
            Guid.NewGuid().ToString("N"),
            key,
            valueJson,
            now), cancellationToken);
    }
}
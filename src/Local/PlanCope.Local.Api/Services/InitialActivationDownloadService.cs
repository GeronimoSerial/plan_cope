using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Services;

public interface IInitialActivationDownloadService
{
    Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class InitialActivationDownloadService(
    IOutboxRepository outboxRepository,
    LocalOutboxPushService outboxPushService,
    LocalExamPullService examPullService,
    LocalRosterPullService rosterPullService,
    ActivationRevalidationService revalidationService) : IInitialActivationDownloadService
{
    public async Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default)
    {
        // An expiry wipe commits before asset files can be removed. Resume the idempotent
        // cleanup before writing any assets from the reactivated node.
        if (await revalidationService.IsExpiredAsync(cancellationToken))
            await revalidationService.CompletePendingAssetCleanupAsync(cancellationToken);

        // Give preserved results first chance to sync before pulling fresh school data. A rejected
        // row or a row in backoff must never block reactivation; the normal sync loop retries it.
        if (await outboxRepository.CountPendingAsync(cancellationToken) > 0)
        {
            LocalOutboxPushResult pushed;
            var pushCompleted = false;
            try
            {
                pushed = await outboxPushService.PushAsync(200, cancellationToken);
                pushCompleted = true;
            }
            finally
            {
                if (!pushCompleted)
                    await revalidationService.SetActivationInProgressAsync(false, CancellationToken.None);
            }
            if (pushed.TransportOrAuthFailure)
            {
                await revalidationService.SetActivationInProgressAsync(false, cancellationToken);
                return new(false, "La clave se validó, pero hay resultados pendientes que no se pudieron enviar. Reintentá cuando vuelva la conexión.");
            }
        }

        var exams = await examPullService.PullAsync(cancellationToken);
        if (!exams.Success)
            return new(false, "La activación se guardó, pero no se pudieron descargar las evaluaciones. Reintentá cuando vuelva la conexión.");

        var rosters = await rosterPullService.PullAllAsync(cancellationToken);
        if (!rosters.Success)
            return new(false, "La activación se guardó, pero no se pudieron descargar todas las escuelas y listas. Reintentá cuando vuelva la conexión.");

        await revalidationService.ClearExpiredFlagAsync(cancellationToken);
        await revalidationService.SetExpiryPendingAsync(false, cancellationToken);
        await revalidationService.SetActivationInProgressAsync(false, cancellationToken);
        return new(true, null);
    }
}

public sealed record InitialActivationDownloadResult(bool Success, string? Error);

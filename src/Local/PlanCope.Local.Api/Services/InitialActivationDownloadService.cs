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

        // On reactivation, preserved results must reach Central before fresh school data is pulled.
        for (var batch = 0; batch < 100; batch++)
        {
            var before = await outboxRepository.CountPendingAsync(cancellationToken);
            if (before == 0) break;
            var pushed = await outboxPushService.PushAsync(200, cancellationToken);
            var after = await outboxRepository.CountPendingAsync(cancellationToken);
            if (after == 0) break;
            if (after >= before || pushed.Accepted == 0)
                return new(false, "La clave se validó, pero hay resultados pendientes que no se pudieron enviar. Reintentá cuando vuelva la conexión.");
        }
        if (await outboxRepository.CountPendingAsync(cancellationToken) > 0)
            return new(false, "Quedan resultados pendientes de enviar. Reintentá la activación.");

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

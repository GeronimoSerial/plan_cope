using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Services;

public interface IInitialActivationDownloadService
{
    Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class InitialActivationDownloadService(
    IOutboxRepository outboxRepository,
    ILocalOutboxPushService outboxPushService,
    ILocalExamPullService examPullService,
    ILocalRosterPullService rosterPullService,
    ActivationRevalidationService revalidationService,
    ILogger<InitialActivationDownloadService> logger) : IInitialActivationDownloadService
{
    public async Task<InitialActivationDownloadResult> DownloadAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await DownloadCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial activation download failed unexpectedly.");
            var reason = LocalRosterPullService.SummarizeFailure(exception.Message);
            return new(false, $"No se pudo completar la descarga inicial. {reason} Reintentá la descarga.");
        }
    }

    private async Task<InitialActivationDownloadResult> DownloadCoreAsync(CancellationToken cancellationToken)
    {
        // An expiry wipe commits before asset files can be removed. Resume the idempotent
        // cleanup before writing any assets from the reactivated node.
        if (await revalidationService.IsExpiredAsync(cancellationToken))
            await revalidationService.CompletePendingAssetCleanupAsync(cancellationToken);

        // Give preserved results first chance to sync before pulling fresh school data. A rejected
        // row or a row in backoff must never block reactivation; the normal sync loop retries it.
        if (await outboxRepository.CountPendingAsync(cancellationToken) > 0)
        {
            var pushed = await outboxPushService.PushAsync(200, cancellationToken);
            if (pushed.TransportOrAuthFailure)
            {
                logger.LogError("Initial activation download could not push pending results: {Error}", pushed.Error);
                return new(false, "La clave se validó, pero hay resultados pendientes que no se pudieron enviar. Reintentá cuando vuelva la conexión.");
            }
        }

        var exams = await examPullService.PullAsync(cancellationToken);
        if (!exams.Success)
        {
            var reason = exams.Error ?? ExamPullMessages.ForError(exams.ErrorCode);
            logger.LogError("Initial activation exam pull failed with {ErrorCode}: {Error}", exams.ErrorCode, reason);
            return new(false, $"La activación se guardó, pero no se pudieron descargar las evaluaciones. {reason} Reintentá la descarga.");
        }

        var rosters = await rosterPullService.PullAllAsync(cancellationToken);
        if (!rosters.Success)
        {
            var reason = string.IsNullOrWhiteSpace(rosters.Error)
                ? "Reintentá la descarga cuando vuelva la conexión."
                : rosters.Error;
            logger.LogError("Initial activation roster pull failed: {Error}", reason);
            return new(false, $"La activación se guardó, pero no se pudieron descargar todas las escuelas y listas. {reason}");
        }

        await revalidationService.ClearExpiredFlagAsync(cancellationToken);
        await revalidationService.SetExpiryPendingAsync(false, cancellationToken);
        await revalidationService.SetActivationInProgressAsync(false, cancellationToken);
        return new(true, null);
    }
}

public sealed record InitialActivationDownloadResult(bool Success, string? Error);

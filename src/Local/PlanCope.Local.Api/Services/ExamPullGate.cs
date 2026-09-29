namespace PlanCope.Local.Api.Services;

/// <summary>
/// Serialises exam pulls so the on-demand endpoint and the autonomous
/// <see cref="SyncBackgroundService"/> can never import the same Central page at the same
/// time. Registered as a singleton, so every <see cref="LocalExamPullService"/> scope shares
/// the same gate.
/// </summary>
public sealed class ExamPullGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) => semaphore.WaitAsync(cancellationToken);

    public void Release() => semaphore.Release();
}

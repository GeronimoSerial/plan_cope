namespace PlanCope.Local.Api.Services;

/// <summary>
/// Serializes answer writes with every path that builds and persists a submission.
/// Local runs one API process, so a process-wide gate also covers submissions started
/// by teacher session closure and background finalization.
/// </summary>
public static class AttemptMutationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static Task WaitAsync(CancellationToken cancellationToken = default) => Gate.WaitAsync(cancellationToken);

    public static void Release() => Gate.Release();
}

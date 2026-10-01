namespace PlanCope.Local.Host.Services;

public enum UpdateRestartDecision
{
    SessionActive,
    RestartStarted,
    RestartUnavailable,
}

public static class UpdateRestartGuard
{
    public static async Task<UpdateRestartDecision> TryStartAsync(
        Func<Task<bool>> hasActiveSession,
        Func<bool> tryApplyAndRestart)
    {
        if (await hasActiveSession())
        {
            return UpdateRestartDecision.SessionActive;
        }

        return tryApplyAndRestart()
            ? UpdateRestartDecision.RestartStarted
            : UpdateRestartDecision.RestartUnavailable;
    }
}

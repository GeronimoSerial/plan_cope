namespace PlanCope.Central.Api.Services;

public static class SessionHeartbeatPolicy
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
}

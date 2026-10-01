namespace PlanCope.Local.Api.Services;

/// <summary>
/// Serializes the local state changes made by revocation enforcement and successful enrolment.
/// </summary>
public static class NodeRevocationCoordinator
{
    public static SemaphoreSlim Gate { get; } = new(1, 1);
}

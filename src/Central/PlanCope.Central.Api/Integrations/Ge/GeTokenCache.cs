namespace PlanCope.Central.Api.Integrations.Ge;

/// <summary>
/// Estado de token compartido por todas las instancias transient del cliente tipado.
/// El access token sólo permanece en memoria del proceso.
/// </summary>
public sealed class GeTokenCache
{
    internal SemaphoreSlim RefreshLock { get; } = new(1, 1);

    internal string? AccessToken { get; set; }

    internal DateTimeOffset ExpiresAt { get; set; }
}

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Fallback <see cref="IInstallerStorage"/> used when the private installer repo/token is not
/// configured (e.g. local development without the env vars set) or when startup registration
/// intentionally falls back. Always reports "no installer available" so the downloads endpoint
/// returns 503 instead of failing.
/// </summary>
public sealed class NotConfiguredInstallerStorage(ILogger<NotConfiguredInstallerStorage> logger) : IInstallerStorage
{
    public Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken)
    {
        logger.LogWarning("Installer repo/token is not configured — set PLANCOPE_PRIVATE_INSTALLER_REPO / INSTALLER_REPO_TOKEN.");
        return Task.FromResult<InstallerReference?>(null);
    }
}
namespace PlanCope.Central.Api.Services;

/// <summary>
/// Outcome of a release-gate resolution: whether the node may install a target version, plus the
/// artifact to download when it may.
/// </summary>
public sealed record ReleaseGateDecision(bool MayInstall, string? TargetVersion, string? DownloadUrl, string? Sha256)
{
    public static ReleaseGateDecision None => new(false, null, null, null);
}

public interface IReleaseGateService
{
    Task<ReleaseGateDecision> ResolveAsync(string nodeId, string currentVersion, string channel, CancellationToken cancellationToken);
}
namespace PlanCope.Central.Api.Services;

/// <summary>
/// Outcome of a release-gate resolution: whether the node may install a target version, plus the
/// artifact to download when it may.
/// </summary>
public sealed record ReleaseGateDecision(bool MayInstall, string? TargetVersion)
{
    public static ReleaseGateDecision None => new(false, null);
}

public interface IReleaseGateService
{
    Task<ReleaseGateDecision> ResolveAsync(
        string nodeId,
        string currentVersion,
        string channel,
        string? latestPublishedVersion,
        CancellationToken cancellationToken);
}

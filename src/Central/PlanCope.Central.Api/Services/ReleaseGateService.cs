using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Decides whether a registered node may install a release version by resolving the newest
/// <c>release_rings</c> row for the requested channel and applying that ring's rollout policy.
/// </summary>
public sealed class ReleaseGateService(PlanCopeDbContext dbContext) : IReleaseGateService
{
    public async Task<ReleaseGateDecision> ResolveAsync(string nodeId, string currentVersion, string channel, CancellationToken cancellationToken)
    {
        var node = await dbContext.RegisteredNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == nodeId, cancellationToken);

        if (node is null)
        {
            return ReleaseGateDecision.None;
        }

        var ring = await dbContext.Set<ReleaseRing>()
            .AsNoTracking()
            .Where(x => x.Channel == channel)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (ring is null || string.Equals(ring.Version, currentVersion, StringComparison.Ordinal))
        {
            return ReleaseGateDecision.None;
        }

        var eligible = ring.RolloutMode switch
        {
            "AllEnrolled" => true,
            "PercentageOfEnrolled" => IsNodeInPercentage(node.Id, ring),
            // Explicit-list rollout is out of scope for this task: the membership table does not
            // exist yet, so no node is eligible until a sibling task introduces it.
            "ExplicitList" => false,
            _ => false
        };

        return eligible
            ? new ReleaseGateDecision(true, ring.Version, ring.DownloadUrl, ring.Sha256)
            : ReleaseGateDecision.None;
    }

    // Deterministic per (node, ring): SHA-256 over "nodeId:ringId" interpreted as a big-endian
    // uint and reduced mod 100, compared against the ring's RolloutPercentage. The same node
    // always maps to the same bucket for the same ring — no random source is involved, so the
    // in/out answer never flips between calls.
    private static bool IsNodeInPercentage(string nodeId, ReleaseRing ring)
    {
        var percentage = Math.Clamp(ring.RolloutPercentage ?? 0, 0, 100);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{nodeId}:{ring.Id}"));
        var bucket = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
        return bucket % 100 < (uint)percentage;
    }
}
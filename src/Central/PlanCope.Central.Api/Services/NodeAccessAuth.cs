using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// The single definition of the node-access-token check used by the update endpoints. A node
/// presents a bearer token whose <c>token_type</c> is <c>node_access</c> and that carries a
/// non-empty <c>node_id</c> claim. A valid user/operator token (any other <c>token_type</c>) must
/// be rejected with 403, not 401 — the token is valid, just not privileged for these endpoints.
/// </summary>
public static class NodeAccessAuth
{
    public const string TokenTypeClaim = "token_type";
    public const string NodeAccessTokenType = "node_access";
    public const string NodeIdClaim = "node_id";

    /// <summary>
    /// Resolves the caller's node id from the validated JWT claims. Returns <c>false</c> (and a
    /// possibly-non-null <paramref name="nodeId"/>) when the token type or node id claim does not
    /// satisfy the node-access check.
    /// </summary>
    public static bool TryGetNodeId(ClaimsPrincipal user, [NotNullWhen(true)] out string? nodeId)
    {
        var tokenType = user.FindFirst(TokenTypeClaim)?.Value;
        nodeId = user.FindFirst(NodeIdClaim)?.Value;

        return string.Equals(tokenType, NodeAccessTokenType, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(nodeId);
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Controllers;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Shared principal wiring for the sync controller tests. <c>Pull</c>/<c>Push</c> now resolve the
/// node identity from the validated JWT, so a direct-construction test must supply a claims
/// principal instead of relying on the query/header/body id alone.
/// </summary>
internal static class SyncTestPrincipals
{
    public const string NodeAccessTokenType = "node_access";

    public static ClaimsPrincipal Node(string nodeId) => Principal(NodeAccessTokenType, nodeId);

    public static ClaimsPrincipal User() => Principal("access", nodeId: null);

    public static ClaimsPrincipal Principal(string? tokenType, string? nodeId)
    {
        var claims = new List<Claim>();
        if (tokenType is not null)
        {
            claims.Add(new Claim("token_type", tokenType));
        }

        if (nodeId is not null)
        {
            claims.Add(new Claim("node_id", nodeId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    public static void BindNode(SyncController controller, string nodeId) => Bind(controller, Node(nodeId));

    public static void Bind(SyncController controller, ClaimsPrincipal user)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }
}

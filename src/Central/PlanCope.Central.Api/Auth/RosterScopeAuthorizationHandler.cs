using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Auth;

public sealed class RosterScopeRequirement : IAuthorizationRequirement;

public sealed class RosterScopeAuthorizationHandler : AuthorizationHandler<RosterScopeRequirement, string>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RosterScopeRequirement requirement,
        string resource)
    {
        var scope = context.User.FindFirstValue("roster_scope");
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Task.CompletedTask;
        }

        if (string.Equals(scope, "province", StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (string.Equals(scope, "school", StringComparison.Ordinal))
        {
            foreach (var cueClaim in context.User.FindAll("roster_cue"))
            {
                if (CueCode.TryNormalize(resource, out var requestedCue) &&
                    CueCode.TryNormalize(cueClaim.Value, out var assignedCue) &&
                    string.Equals(requestedCue, assignedCue, StringComparison.Ordinal))
                {
                    context.Succeed(requirement);
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }
}
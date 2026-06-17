using System.Security.Claims;
using PlanCope.Shared.Contracts.Auth;

namespace PlanCope.Central.Api.Auth;

public interface ITokenService
{
    string CreateAccessToken(UserProfileDto user);

    string CreateRefreshToken(UserProfileDto user);

    ClaimsPrincipal ValidateRefreshToken(string refreshToken);
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PlanCope.Shared.Contracts.Auth;

namespace PlanCope.Central.Api.Auth;

public sealed class TokenService(IOptions<AuthOptions> options) : ITokenService
{
    private const string TokenTypeClaim = "token_type";
    private const string RefreshTokenType = "refresh";
    private readonly AuthOptions _options = options.Value;

    public string CreateAccessToken(UserProfileDto user)
    {
        var claims = CreateBaseClaims(user);
        return CreateToken(claims, TimeSpan.FromMinutes(_options.AccessTokenMinutes));
    }

    public string CreateRefreshToken(UserProfileDto user)
    {
        var claims = CreateBaseClaims(user);
        claims.Add(new Claim(TokenTypeClaim, RefreshTokenType));

        return CreateToken(claims, TimeSpan.FromDays(_options.RefreshTokenDays));
    }

    public ClaimsPrincipal ValidateRefreshToken(string refreshToken)
    {
        var principal = new JwtSecurityTokenHandler().ValidateToken(refreshToken, CreateValidationParameters(), out _);
        var tokenType = principal.FindFirstValue(TokenTypeClaim);

        if (!string.Equals(tokenType, RefreshTokenType, StringComparison.Ordinal))
        {
            throw new SecurityTokenException("Token is not a refresh token.");
        }

        return principal;
    }

    private string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(CreateSigningKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private SymmetricSecurityKey CreateSigningKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    private static List<Claim> CreateBaseClaims(UserProfileDto user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role)
        };

        if (!string.IsNullOrWhiteSpace(user.SchoolId))
        {
            claims.Add(new Claim("school_id", user.SchoolId));
        }

        return claims;
    }
}

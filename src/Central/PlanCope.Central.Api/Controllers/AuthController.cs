using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Auth;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(PlanCopeDbContext dbContext, ITokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var user = await dbContext.Users
            .Where(x => x.Email == username && x.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || !IsActive(user.Status) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized();
        }

        var role = await GetPrimaryRoleAsync(user.Id, cancellationToken);
        var cues = await dbContext.UserSchools
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.Cue)
            .Select(x => x.Cue)
            .ToListAsync(cancellationToken);

        var rosterScope = string.Equals(role, "RosterProvince", StringComparison.Ordinal) ? "province" : "school";
        IReadOnlyList<string> rosterCues = rosterScope == "province" ? [] : cues;
        var profile = new UserProfileDto(user.Id, user.FullName, role, null, rosterScope, rosterCues);

        return Ok(new LoginResponse(
            tokenService.CreateAccessToken(profile),
            tokenService.CreateRefreshToken(profile),
            profile));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ClaimsPrincipal principal;

        try
        {
            principal = tokenService.ValidateRefreshToken(request.RefreshToken);
        }
        catch (SecurityTokenException)
        {
            return Unauthorized();
        }
        catch (ArgumentException)
        {
            return Unauthorized();
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await dbContext.Users
            .Where(x => x.Id == userId && x.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || !IsActive(user.Status))
        {
            return Unauthorized();
        }

        var role = await GetPrimaryRoleAsync(user.Id, cancellationToken);
        var cues = await dbContext.UserSchools
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.Cue)
            .Select(x => x.Cue)
            .ToListAsync(cancellationToken);

        var rosterScope = string.Equals(role, "RosterProvince", StringComparison.Ordinal) ? "province" : "school";
        IReadOnlyList<string> rosterCues = rosterScope == "province" ? [] : cues;
        var profile = new UserProfileDto(user.Id, user.FullName, role, null, rosterScope, rosterCues);

        return Ok(new LoginResponse(
            tokenService.CreateAccessToken(profile),
            tokenService.CreateRefreshToken(profile),
            profile));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserProfileDto> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);
        var schoolId = User.FindFirstValue("school_id");
        var rosterScope = User.FindFirstValue("roster_scope") ?? "school";
        IReadOnlyList<string> rosterCues = rosterScope == "province"
            ? []
            : User.FindAll("roster_cue").Select(static x => x.Value).ToList();

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(role))
        {
            return Unauthorized();
        }

        return Ok(new UserProfileDto(userId, displayName, role, schoolId, rosterScope, rosterCues));
    }

    private async Task<string> GetPrimaryRoleAsync(string userId, CancellationToken cancellationToken)
    {
        var role = await (
            from userRole in dbContext.UserRoles
            join roleRow in dbContext.Roles on userRole.RoleId equals roleRow.Id
            where userRole.UserId == userId
            orderby roleRow.Code
            select roleRow.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return role ?? "Viewer";
    }

    private static bool IsActive(string status)
    {
        return string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
    }
}

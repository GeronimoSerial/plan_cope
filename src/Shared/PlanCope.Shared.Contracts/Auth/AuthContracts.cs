namespace PlanCope.Shared.Contracts.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    [property: System.Text.Json.Serialization.JsonRequired] string AccessToken,
    string? RefreshToken,
    UserProfileDto User);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record UserProfileDto(
    string Id,
    string DisplayName,
    string Role,
    string? SchoolId,
    string RosterScope,
    IReadOnlyList<string> RosterCues);

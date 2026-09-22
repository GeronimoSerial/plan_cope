namespace PlanCope.Shared.Contracts.Admin;

/// <summary>Body of the create-user endpoint. Password is hashed with BCrypt on the server and
/// is never stored or returned in plaintext.</summary>
public sealed record UserCreateRequest(string Email, string Password, string FullName);

/// <summary>User row for the admin surface. Deliberately closed set: no password hash and no
/// plaintext password ever appears here.</summary>
public sealed record UserSummaryDto(string Id, string Email, string FullName, string Status, IReadOnlyList<string> Cues, IReadOnlyList<string> RoleCodes);

/// <summary>Body of the reset-password endpoint. NewPassword is minimum 8 characters and is
/// never echoed back — the response is always 204.</summary>
public sealed record ResetPasswordRequest(string NewPassword);

/// <summary>Body of the assign-role endpoint. RoleCode is Role.Code, not the role id.</summary>
public sealed record AssignRoleRequest(string RoleCode);

/// <summary>Body of the assign-CUE endpoint. Cue is normalized to its 9-digit form before the
/// assignment row is written.</summary>
public sealed record AssignSchoolRequest(string Cue);

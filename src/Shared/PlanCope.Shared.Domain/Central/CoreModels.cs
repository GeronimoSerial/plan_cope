using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

public sealed record User(string Id, string Email, string PasswordHash, string FullName, string Status, DateTimeOffset? LastLoginAt, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record Role(string Id, string Code, string Name, string? Description, DateTimeOffset CreatedAt);

public sealed record UserRoleAssignment(string UserId, string RoleId, DateTimeOffset CreatedAt);

public sealed record Province(string Id, string Code, string Name, DateTimeOffset CreatedAt);

public sealed record Department(string Id, string Code, string Name, string ProvinceId, DateTimeOffset CreatedAt);

public sealed record Locality(string Id, string DepartmentId, string Code, string? PostalCode, string Name, DateTimeOffset CreatedAt);

public sealed record School(string Id, string Code, long Cue, int? Annex, string Name, string LocalityId, string Status, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record Classroom(string Id, string SchoolId, string Code, string Name, string? Shift, JsonDocument? Metadata, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

namespace PlanCope.Shared.Contracts.Admin;

/// <summary>Role row for the admin list endpoint — read-only reference data used to build an
/// assign-role request.</summary>
public sealed record RoleSummaryDto(string Id, string Code, string Name, string? Description);

namespace PlanCope.Shared.Domain.Central;

public sealed record GradingPolicyAssignment(
    string Id,
    string ExamVersionId,
    string ScoringPolicy,
    string AssignedBy,
    DateTimeOffset AssignedAt,
    string? Note);

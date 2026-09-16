namespace PlanCope.Shared.Contracts.Exams;

public sealed record UnassignedExamVersionDto(
    string ExamVersionId,
    string ExamCode,
    int VersionNumber,
    DateTimeOffset? PublishedAt);

public sealed record BulkAssignGradingPolicyRequest(IReadOnlyList<string> ExamVersionIds, string ScoringPolicy, string? Note);

public sealed record BulkAssignGradingPolicyResult(int AssignedCount, IReadOnlyList<string> RejectedExamVersionIds);
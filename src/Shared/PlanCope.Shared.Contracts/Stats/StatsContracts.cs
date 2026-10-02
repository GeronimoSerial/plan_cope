namespace PlanCope.Shared.Contracts.Stats;

public sealed record StatsMetricDto<T>(T? Value, string Status) where T : struct;
public sealed record StatsPageDto<T>(int Page, int PageSize, int TotalCount, IReadOnlyList<T> Items);
public sealed record StatsOptionDto(string Value, string Label);
public sealed record StatsSummaryDto(StatsMetricDto<int> PublishedExams, StatsMetricDto<int> SchoolsWithResults, StatsMetricDto<int> GradedAttempts, StatsMetricDto<int> FreshSessions, StatsMetricDto<int>? PendingAttribution, string GeneratedAt, string? LatestRollupUpdatedAt);
public sealed record StatsAggregateRowDto(string Key, string Label, StatsMetricDto<int> AttemptCount, StatsMetricDto<double> WeightedScorePercent);
public sealed record StatsAggregateDto(string DataState, string GeneratedAt, string? LatestRollupUpdatedAt, string Dimension, IReadOnlyDictionary<string, string?> Filters, IReadOnlyList<StatsAggregateRowDto> Rows, int Page, int PageSize, int TotalCount);
public sealed record SchoolStatsDetailDto(string Cue, string? SchoolName, string? Locality, string? Department, StatsMetricDto<int> AttemptCount, StatsMetricDto<double> AverageScorePercent, string? LatestRollupUpdatedAt);
public sealed record SchoolStatsListItemDto(
    string Cue,
    string SchoolName,
    StatsMetricDto<int> AttemptCount,
    StatsMetricDto<double> AverageScorePercent,
    bool FreshSession,
    string? LatestRollupUpdatedAt,
    int? Annex = null,
    string? LocalityId = null,
    string? Locality = null,
    string? DepartmentId = null,
    string? Department = null);

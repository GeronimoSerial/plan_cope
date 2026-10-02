namespace PlanCope.Shared.Contracts.Stats;

public sealed record StatsShareCreateRequest(
    string GroupBy,
    IReadOnlyDictionary<string, string?> Filters,
    IReadOnlyList<string>? Metrics = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record StatsShareCreatedDto(string Id, string Url, string ExpiresAt);

public sealed record PublicStatsShareDto(
    string GroupBy,
    string GroupLabel,
    IReadOnlyDictionary<string, string> Filters,
    IReadOnlyList<PublicStatsShareRowDto> Rows,
    PublicStatsShareMetricDto TotalAttempts,
    PublicStatsShareMetricDto TotalWeightedScorePercent,
    string GeneratedAt,
    string ExpiresAt);

public sealed record PublicStatsShareRowDto(
    string Label,
    PublicStatsShareMetricDto AttemptCount,
    PublicStatsShareMetricDto WeightedScorePercent);

public sealed record PublicStatsShareMetricDto(double? Value, string Status);

namespace PlanCope.Shared.Contracts.Admin;

/// <summary>Body of the create-release-ring endpoint. Version, Channel, Sha256 and DownloadUrl are
/// immutable identifying/artifact fields set at creation; RolloutMode and RolloutPercentage define
/// the rollout policy and are the only fields the update endpoint may change later.</summary>
public sealed record ReleaseRingCreateRequest(string Version, string Channel, string Sha256, string DownloadUrl, string RolloutMode, int? RolloutPercentage);

/// <summary>Body of the release-ring update endpoint. Version, Channel, Sha256 and DownloadUrl are
/// immutable — an update can only change RolloutMode and RolloutPercentage.</summary>
public sealed record ReleaseRingUpdateRequest(string RolloutMode, int? RolloutPercentage);

/// <summary>Release ring row for the admin surface: the newest row per Channel is the one
/// ReleaseGateService consults when a node asks to install.</summary>
public sealed record ReleaseRingSummaryDto(Guid Id, string Version, string Channel, string Sha256, string DownloadUrl, string RolloutMode, int? RolloutPercentage, DateTimeOffset CreatedAt, Guid CreatedBy);

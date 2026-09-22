namespace PlanCope.Shared.Contracts.Admin;

/// <summary>Body of the create-school endpoint. Cue is accepted in any textual form (dashes and
/// whitespace allowed) and is normalized to its 9-digit form before the school row is written.</summary>
public sealed record SchoolCreateRequest(string Cue, string Code, string Name, string LocalityId, int? Annex);

/// <summary>Body of the school update endpoint. Cue and Code are immutable — an update can only
/// change Name, Annex and LocalityId.</summary>
public sealed record SchoolUpdateRequest(string Name, int? Annex, string LocalityId);

/// <summary>School row for the admin surface. Cue is the normalized string form of the long
/// stored in the schools table.</summary>
public sealed record SchoolSummaryDto(string Id, string Cue, string Code, string Name, string LocalityId, int? Annex, string Status);

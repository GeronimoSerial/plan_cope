using System.Text.Json;

namespace PlanCope.Shared.Domain.Central;

public sealed record PublicationPackage(string Id, string ExamVersionId, int PackageVersion, string Checksum, JsonDocument Manifest, string Status, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record PublicationTarget(string Id, string PublicationPackageId, string TargetType, string? TargetId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

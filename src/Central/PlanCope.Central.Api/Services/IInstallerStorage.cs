namespace PlanCope.Central.Api.Services;

public interface IInstallerStorage
{
    Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken);
}

public sealed record InstallerReference(string Version, string Channel, Uri DownloadUrl, string Sha256, DateTimeOffset PublishedAt);
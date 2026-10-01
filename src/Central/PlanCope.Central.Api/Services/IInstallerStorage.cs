using System.Net.Http;

namespace PlanCope.Central.Api.Services;

public interface IInstallerStorage
{
    /// <summary>
    /// True when the backend has enough configuration (repo + token) to query GitHub at all.
    /// Callers use this to distinguish "not configured" (503) from "configured, nothing to
    /// serve" (404) — two different operator-facing situations.
    /// </summary>
    bool IsConfigured { get; }

    Task<InstallerReference?> GetLatestAsync(string channel, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the installer asset bytes for the latest release on <paramref name="channel"/>.
    /// Returns null when configured but no matching installer asset exists (e.g. the release only
    /// carries the encrypted roster bundle). The caller owns and must dispose the result.
    /// </summary>
    Task<InstallerDownload?> GetLatestDownloadAsync(string channel, CancellationToken cancellationToken);

    Task<InstallerDownload?> GetAssetDownloadAsync(string assetName, CancellationToken cancellationToken);
}

public sealed record InstallerReference(string Version, string Channel, Uri DownloadUrl, string Sha256, DateTimeOffset PublishedAt);

/// <summary>
/// A streamable installer asset. Disposing this disposes the underlying <see cref="HttpResponseMessage"/>,
/// which owns <see cref="Content"/> — callers must not read <see cref="Content"/> after disposal.
/// </summary>
public sealed class InstallerDownload(
    HttpResponseMessage response,
    Stream content,
    string contentType,
    string fileName,
    long? contentLength) : IDisposable
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    public string FileName { get; } = fileName;

    public long? ContentLength { get; } = contentLength;

    public void Dispose() => response.Dispose();
}

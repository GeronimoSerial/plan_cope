using System.Security.Cryptography;
using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Services;

public sealed class LocalAssetFileService(IConfiguration configuration, ILocalExamRepository repository)
{
    public async Task<SavedLocalAsset> SaveAsync(string assetId, string fileName, string mimeType, byte[] bytes, CancellationToken cancellationToken)
    {
        var assetsRoot = GetAssetsRoot();
        Directory.CreateDirectory(assetsRoot);

        var extension = Path.GetExtension(fileName);
        var storedFileName = string.IsNullOrWhiteSpace(extension) ? assetId : $"{assetId}{extension}";
        var localPath = Path.Combine(assetsRoot, storedFileName);

        await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);

        return new SavedLocalAsset(localPath, mimeType, HexSha256(bytes));
    }

    public async Task<ResolvedLocalAsset?> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        if (id.Contains('/') || id.Contains('\\'))
        {
            return null;
        }

        var asset = await repository.GetAssetByIdAsync(id, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(GetAssetsRoot()));
        var fullPath = Path.GetFullPath(asset.LocalPath);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return null;
        }

        return new ResolvedLocalAsset(fullPath, asset.MimeType);
    }

    public string GetAssetsRoot()
    {
        var configured = configuration.GetValue<string>("Local:AssetsPath");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "local-assets")
            : configured;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string HexSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

public sealed record SavedLocalAsset(string Path, string MimeType, string Checksum);

public sealed record ResolvedLocalAsset(string Path, string MimeType);

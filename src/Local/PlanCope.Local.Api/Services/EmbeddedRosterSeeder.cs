using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.RosterCrypto;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Local.Api.Services;

public interface IEmbeddedRosterSource
{
    Task<IReadOnlyList<GeRosterPackageDto>> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class RosterBundleOptions
{
    public const string SectionName = "RosterBundle";
    public string Path { get; init; } = string.Empty;
    public string Cue { get; init; } = string.Empty;
    public string Passphrase { get; init; } = string.Empty;
}

public sealed class EmbeddedRosterSource(IOptions<RosterBundleOptions> options) : IEmbeddedRosterSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GeRosterPackageDto>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.Path) && string.IsNullOrWhiteSpace(configured.Cue) &&
            string.IsNullOrWhiteSpace(configured.Passphrase))
        {
            return [];
        }
        if (string.IsNullOrWhiteSpace(configured.Path) || string.IsNullOrWhiteSpace(configured.Cue) ||
            string.IsNullOrWhiteSpace(configured.Passphrase))
        {
            throw new InvalidOperationException("RosterBundle:Path, Cue and Passphrase must all be configured.");
        }

        var json = await EnvelopeDecryption.DecryptCueAsync(configured.Path, configured.Cue, configured.Passphrase, cancellationToken);
        try
        {
            var package = JsonSerializer.Deserialize<GeRosterPackageDto>(json, JsonOptions)
                ?? throw new InvalidDataException($"Encrypted roster entry for CUE '{configured.Cue}' is empty.");
            LocalRosterPackageValidator.Validate(package);
            if (!string.Equals(package.Cue, configured.Cue, StringComparison.Ordinal))
                throw new InvalidDataException("Decrypted roster CUE does not match the requested CUE.");
            return [package];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }
}

public sealed class EmbeddedRosterSeeder(
    IEmbeddedRosterSource source,
    ILocalRosterRepository repository,
    IDocumentHmacService documentHmacService,
    ILogger<EmbeddedRosterSeeder> logger)
{
    public async Task<EmbeddedRosterSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var packages = await source.ReadAllAsync(cancellationToken);
        var imported = 0;
        foreach (var package in packages)
        {
            var result = await repository.ImportAsync(package, documentHmacService, cancellationToken);
            if (result.Imported)
            {
                imported++;
            }
        }

        if (packages.Count > 0)
        {
            logger.LogInformation(
                "Loaded {PackageCount} encrypted roster packages; {ImportedCount} were new.",
                packages.Count,
                imported);
        }

        return new EmbeddedRosterSeedResult(packages.Count, imported);
    }
}

public sealed record EmbeddedRosterSeedResult(int PackageCount, int ImportedCount);

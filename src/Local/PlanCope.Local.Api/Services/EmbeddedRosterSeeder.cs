using System.Reflection;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Local.Api.Services;

public interface IEmbeddedRosterSource
{
    Task<IReadOnlyList<GeRosterPackageDto>> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class EmbeddedRosterSource : IEmbeddedRosterSource
{
    private const string ResourceMarker = ".Rosters.";
    private const string ResourceSuffix = ".roster.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GeRosterPackageDto>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(EmbeddedRosterSource).Assembly;
        var packages = new List<GeRosterPackageDto>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(static name => name.Contains(ResourceMarker, StringComparison.Ordinal) &&
                                           name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded roster resource '{resourceName}' could not be opened.");
            var package = await JsonSerializer.DeserializeAsync<GeRosterPackageDto>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"Embedded roster resource '{resourceName}' is empty.");
            LocalRosterPackageValidator.Validate(package);
            packages.Add(package);
        }

        return packages;
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
                "Loaded {PackageCount} embedded roster packages; {ImportedCount} were new.",
                packages.Count,
                imported);
        }

        return new EmbeddedRosterSeedResult(packages.Count, imported);
    }
}

public sealed record EmbeddedRosterSeedResult(int PackageCount, int ImportedCount);

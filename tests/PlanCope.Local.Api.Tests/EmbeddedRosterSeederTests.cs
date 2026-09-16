using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.RosterCrypto;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class EmbeddedRosterSeederTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"embedded-roster-{Guid.NewGuid():N}");
    public EmbeddedRosterSeederTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task EmbeddedRosterSourceReadsRequestedCueFromEncryptedBundle()
    {
        var package = CreatePackage();
        var input = Path.Combine(directory, "input");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "180000100-2026.roster.json"), JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var bundle = Path.Combine(directory, "rosters.enc");
        await EnvelopeEncryption.EncryptDirectoryAsync(input, bundle, "test-only", new Argon2Parameters(8, 1, 1));
        var source = new EmbeddedRosterSource(Options.Create(new RosterBundleOptions
        {
            Path = bundle, Passphrase = "test-only"
        }));

        var result = await source.ReadOneAsync("180000100", "test-only");

        Assert.NotNull(result);
        Assert.Equal("180000100", result.Cue);
        Assert.Equal("99000001", Assert.Single(Assert.Single(result.Sections).Students).Document);
    }

    [Fact]
    public async Task EmbeddedRosterSourceReadOneReturnsNullWhenNoBundlePathConfigured()
    {
        var source = new EmbeddedRosterSource(Options.Create(new RosterBundleOptions()));
        var result = await source.ReadOneAsync("180000100", "test-only");
        Assert.Null(result);
    }

    [Fact]
    public async Task EmbeddedRosterSourceReadAllReturnsEmptyWithoutCallerSuppliedCue()
    {
        var source = new EmbeddedRosterSource(Options.Create(new RosterBundleOptions { Path = "missing.enc" }));
        var result = await source.ReadAllAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task SeedOneAsyncImportsPackageAndReportsImportedCount()
    {
        var package = CreatePackage();
        var source = new StubEmbeddedRosterSource(package);
        var repository = new RecordingRosterRepository(imported: true);
        var seeder = new EmbeddedRosterSeeder(source, repository, new StubHmacService(), NullLogger<EmbeddedRosterSeeder>.Instance);

        var result = await seeder.SeedOneAsync("180000100", "test-only");

        Assert.Equal(1, result.PackageCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Same(package, repository.ImportedPackage);
    }

    [Fact]
    public async Task SeedOneAsyncSkipsImportWhenSourceReturnsNull()
    {
        var source = new StubEmbeddedRosterSource(null);
        var repository = new RecordingRosterRepository(imported: false);
        var seeder = new EmbeddedRosterSeeder(source, repository, new StubHmacService(), NullLogger<EmbeddedRosterSeeder>.Instance);

        var result = await seeder.SeedOneAsync("180000100", "test-only");

        Assert.Equal(0, result.PackageCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Null(repository.ImportedPackage);
    }

    private static GeRosterPackageDto CreatePackage()
    {
        var section = new GeRosterSectionPackageDto("synthetic-section", 900001, "1", "A", "Primario", "Mañana",
            [new("synthetic-student", "synthetic-section", 910001, "99000001", "Ada", "Ejemplo")]);
        var withoutChecksum = new GeRosterPackageDto("synthetic-snapshot", "180000100", "2026",
            DateTimeOffset.Parse("2026-01-15T12:00:00Z"), new string('0', 64), 1, 1, "available", [section], "Escuela Sintética");
        return withoutChecksum with { Checksum = GeRosterPackageChecksum.Calculate(withoutChecksum) };
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed class StubEmbeddedRosterSource(GeRosterPackageDto? package) : IEmbeddedRosterSource
    {
        public Task<IReadOnlyList<GeRosterPackageDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeRosterPackageDto>>(package is null ? [] : [package]);

        public Task<GeRosterPackageDto?> ReadOneAsync(string cue, string passphrase, CancellationToken cancellationToken = default) =>
            Task.FromResult(package);
    }

    private sealed class RecordingRosterRepository(bool imported) : ILocalRosterRepository
    {
        public GeRosterPackageDto? ImportedPackage { get; private set; }

        public Task<LocalRosterImportResult> ImportAsync(
            GeRosterPackageDto package,
            IDocumentHmacService documentHmacService,
            CancellationToken cancellationToken = default)
        {
            ImportedPackage = package;
            return Task.FromResult(new LocalRosterImportResult(imported, package.SnapshotId, package.Checksum, 1, 1));
        }

        public Task<LocalRosterStudentLookup?> FindStudentAsync(
            string snapshotId, string sectionId, string document, IDocumentHmacService documentHmacService, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LocalRosterSnapshotLookup?> GetLatestSnapshotAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LocalRosterSnapshotLookup?> GetLatestSnapshotAsync(string cue, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<LocalRosterSectionLookup>> GetSectionsAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LocalRosterSelectionValidation> ValidateSelectionAsync(string cue, string schoolYear, string snapshotId, string sectionId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubHmacService : IDocumentHmacService
    {
        public string ComputeHash(string document) => new('a', 64);
        public string ComputeLast4(string document) => "0000";
    }
}
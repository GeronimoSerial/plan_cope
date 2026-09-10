using System.Text.Json;
using Microsoft.Extensions.Options;
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
    public async Task EmbeddedRosterSeederSourceReadsRequestedCueFromEncryptedBundle()
    {
        var section = new GeRosterSectionPackageDto("synthetic-section", 900001, "1", "A", "Primario", "Mañana",
            [new("synthetic-student", "synthetic-section", 910001, "99000001", "Ada", "Ejemplo")]);
        var withoutChecksum = new GeRosterPackageDto("synthetic-snapshot", "180000100", "2026",
            DateTimeOffset.Parse("2026-01-15T12:00:00Z"), new string('0', 64), 1, 1, "available", [section], "Escuela Sintética");
        var package = withoutChecksum with { Checksum = GeRosterPackageChecksum.Calculate(withoutChecksum) };
        var input = Path.Combine(directory, "input");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "180000100-2026.roster.json"), JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var bundle = Path.Combine(directory, "rosters.enc");
        await EnvelopeEncryption.EncryptDirectoryAsync(input, bundle, "test-only", new Argon2Parameters(8, 1, 1));
        var source = new EmbeddedRosterSource(Options.Create(new RosterBundleOptions
        {
            Path = bundle, Cue = "180000100", Passphrase = "test-only"
        }));

        var result = await source.ReadAllAsync();

        var roster = Assert.Single(result);
        Assert.Equal("180000100", roster.Cue);
        Assert.Equal("99000001", Assert.Single(Assert.Single(roster.Sections).Students).Document);
    }

    [Fact]
    public async Task EmbeddedRosterSeederSourceRejectsIncompleteConfiguration()
    {
        var source = new EmbeddedRosterSource(Options.Create(new RosterBundleOptions { Path = "missing.enc" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAllAsync());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

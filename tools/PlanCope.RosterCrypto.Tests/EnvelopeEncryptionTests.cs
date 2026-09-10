using System.Security.Cryptography;
using System.Text;
using PlanCope.RosterCrypto;
using Xunit;

namespace PlanCope.RosterCrypto.Tests;

public sealed class EnvelopeEncryptionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"roster-crypto-tests-{Guid.NewGuid():N}");
    private readonly Argon2Parameters fastParameters = new(8, 1, 1);

    public EnvelopeEncryptionTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task EncryptsWithoutLeakingJsonAndRoundTripsExactly()
    {
        var original = Encoding.UTF8.GetBytes("{\"cue\":\"180000100\",\"document\":\"synthetic-99000001\"}");
        await File.WriteAllBytesAsync(Path.Combine(directory, "180000100-2026.roster.json"), original);
        var bundle = Path.Combine(directory, "bundle.enc");

        await EnvelopeEncryption.EncryptDirectoryAsync(directory, bundle, "test-only", fastParameters);

        Assert.DoesNotContain("document", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(bundle)), StringComparison.Ordinal);
        Assert.Equal(original, await EnvelopeDecryption.DecryptCueAsync(bundle, "180000100", "test-only"));
    }

    [Fact]
    public async Task WrongPassphraseFailsWithoutReturningPlaintext()
    {
        var bundle = await CreateBundleAsync();
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            EnvelopeDecryption.DecryptCueAsync(bundle, "180000100", "incorrect"));
    }

    [Fact]
    public async Task ModifiedCiphertextFailsAuthentication()
    {
        var bundle = await CreateBundleAsync();
        var bytes = await File.ReadAllBytesAsync(bundle);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(bundle, bytes);

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() =>
            EnvelopeDecryption.DecryptCueAsync(bundle, "180000100", "test-only"));
    }

    [Fact]
    public async Task MissingCueHasSpecificDocumentedError()
    {
        var bundle = await CreateBundleAsync();
        var exception = await Assert.ThrowsAsync<RosterEntryNotFoundException>(() =>
            EnvelopeDecryption.DecryptCueAsync(bundle, "180009999", "test-only"));
        Assert.Equal("180009999", exception.Cue);
    }

    private async Task<string> CreateBundleAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "180000100-2026.roster.json"), "{\"document\":\"synthetic-only\"}");
        var bundle = Path.Combine(directory, "bundle.enc");
        await EnvelopeEncryption.EncryptDirectoryAsync(directory, bundle, "test-only", fastParameters);
        return bundle;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

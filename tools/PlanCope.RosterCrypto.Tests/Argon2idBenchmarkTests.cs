using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using PlanCope.RosterCrypto;
using Xunit;
using Xunit.Abstractions;

namespace PlanCope.RosterCrypto.Tests;

public sealed class Argon2idBenchmarkTests
{
    private readonly ITestOutputHelper output;

    public Argon2idBenchmarkTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task BenchmarkArgon2idDerivationOnThisMachine()
    {
        var passphrase = "benchmark-only-passphrase";
        var salt = RandomNumberGenerator.GetBytes(16);
        var cases = new (string Name, int MemoryKiB, int Iterations, int Parallelism)[]
        {
            ("19 MiB / 2 / 1 (code default, OWASP floor)", 19_456, 2, 1),
            ("64 MiB / 3 / 1 (chosen)", 65_536, 3, 1),
            ("128 MiB / 2 / 1", 131_072, 2, 1),
            ("256 MiB / 2 / 1", 262_144, 2, 1),
        };

        foreach (var (name, memoryKiB, iterations, parallelism) in cases)
        {
            Derive(passphrase, salt, memoryKiB, iterations, parallelism);
            var samples = new List<TimeSpan>(5);
            for (var i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                Derive(passphrase, salt, memoryKiB, iterations, parallelism);
                sw.Stop();
                samples.Add(sw.Elapsed);
            }

            var median = samples.OrderBy(static s => s.Ticks).ElementAt(samples.Count / 2);
            output.WriteLine($"{name} -> median {median.TotalMilliseconds:F0} ms; samples: {string.Join(", ", samples.Select(static s => $"{s.TotalMilliseconds:F0}"))}");
        }
    }

    [Fact]
    public async Task BenchmarkChosenParametersThroughProductionDecryptPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"argon2-bench-{Guid.NewGuid():N}");
        var bundle = Path.Combine(Path.GetTempPath(), $"argon2-bench-{Guid.NewGuid():N}.enc");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "180000100-2026.roster.json"), "{\"cue\":\"180000100\",\"document\":\"synthetic\"}");
            await EnvelopeEncryption.EncryptDirectoryAsync(directory, bundle, "benchmark-pass", new Argon2Parameters(65_536, 3, 1));

            var sw = Stopwatch.StartNew();
            var plaintext = await EnvelopeDecryption.DecryptCueAsync(bundle, "180000100", "benchmark-pass");
            sw.Stop();

            output.WriteLine($"end-to-end DecryptCueAsync (64 MiB / 3 / 1) -> {sw.Elapsed.TotalMilliseconds:F0} ms");
            Assert.Contains("synthetic", Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            if (File.Exists(bundle)) File.Delete(bundle);
        }
    }

    private static byte[] Derive(string passphrase, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        // Mirrors RosterBundleFormat.DeriveMasterKey exactly (that method is internal, so
        // the test assembly cannot call it); Konscious Argon2id, 16-byte salt, 32-byte key.
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(32);
    }
}
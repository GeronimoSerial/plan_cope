using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PlanCope.RosterCrypto;

public sealed record Argon2Parameters(int MemorySizeKiB = 19_456, int Iterations = 2, int Parallelism = 1)
{
    internal void Validate()
    {
        if (MemorySizeKiB < 8 * Parallelism || MemorySizeKiB > 1_048_576 ||
            Iterations is < 1 or > 100 || Parallelism is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(Argon2Parameters), "Invalid Argon2id parameters.");
        }
    }
}

public sealed record RosterBundleManifest(int Version, Argon2Parameters Argon2, IReadOnlyList<RosterBundleManifestEntry> Entries);
public sealed record RosterBundleManifestEntry(string Cue, long Offset, int CiphertextLength, string Sha256);

public sealed class RosterEntryNotFoundException(string cue)
    : KeyNotFoundException($"The roster bundle does not contain CUE '{cue}'.")
{
    public string Cue { get; } = cue;
}

internal static class RosterBundleFormat
{
    internal static readonly byte[] Magic = "PCRSTR01"u8.ToArray();
    internal const ushort Version = 1;
    internal const int SaltSize = 16;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int KeySize = 32;
    internal const int ChecksumSize = 32;
    internal const int CueSize = 9;
    internal const int HeaderSize = 8 + 2 + 4 + 4 + 4 + SaltSize + 4;
    internal const int EntryPrefixSize = CueSize + NonceSize + TagSize + NonceSize + TagSize + KeySize + ChecksumSize + 4;

    internal static byte[] DeriveMasterKey(string passphrase, ReadOnlySpan<byte> salt, Argon2Parameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        parameters.Validate();
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt.ToArray(),
            MemorySize = parameters.MemorySizeKiB,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism
        };
        return argon2.GetBytes(KeySize);
    }

    internal static byte[] CueBytes(string cue)
    {
        if (cue.Length != CueSize || !cue.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("CUE must contain exactly 9 ASCII digits.", nameof(cue));
        }
        return Encoding.ASCII.GetBytes(cue);
    }

    internal static byte[] EntryAssociatedData(ReadOnlySpan<byte> cue, ReadOnlySpan<byte> checksum)
    {
        var aad = new byte[Magic.Length + 2 + cue.Length + checksum.Length];
        Magic.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(Magic.Length), Version);
        cue.CopyTo(aad.AsSpan(Magic.Length + 2));
        checksum.CopyTo(aad.AsSpan(Magic.Length + 2 + cue.Length));
        return aad;
    }

    internal static void ReadExactly(BinaryReader reader, Span<byte> destination)
    {
        var read = reader.BaseStream.ReadAtLeast(destination, destination.Length, throwOnEndOfStream: false);
        if (read != destination.Length)
        {
            throw new InvalidDataException("Roster bundle is truncated.");
        }
    }
}

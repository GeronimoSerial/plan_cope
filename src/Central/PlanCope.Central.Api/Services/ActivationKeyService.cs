using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Services;

/// <summary>
/// Issues and validates activation keys in the PCOPE-XXXXX-XXXXX-XXXXX-CC format. Key material is
/// never logged: this service deliberately holds no logger, and key generation returns the
/// plaintext exactly once to its caller.
/// </summary>
public sealed class ActivationKeyService
{
    // Crockford base32 alphabet (excludes I, L, O, U). Input is case-insensitive; output is uppercase.
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const string Brand = "PCOPE";
    private const int BrandLength = 5;
    private const int PayloadLength = 15;
    private const int ChecksumLength = 2;
    private const int KeyLength = BrandLength + PayloadLength + ChecksumLength;
    private const int PrefixLength = 8;

    // Argon2id parameters mirror the conservatism already used by tools/PlanCope.RosterCrypto.
    private const int Argon2MemorySizeKiB = 19_456;
    private const int Argon2Iterations = 2;
    private const int Argon2Parallelism = 1;
    private const int Argon2SaltSize = 16;
    private const int Argon2HashSize = 32;

    public sealed record GeneratedActivationKey(string PlaintextKey, string KeyHash, string KeyPrefix);

    /// <summary>
    /// Generates a fresh activation key. The plaintext is returned exactly once, in
    /// <see cref="GeneratedActivationKey.PlaintextKey"/>; only <see cref="GeneratedActivationKey.KeyHash"/>
    /// and <see cref="GeneratedActivationKey.KeyPrefix"/> should be persisted.
    /// </summary>
    public GeneratedActivationKey Generate()
    {
        var payload = GeneratePayload();
        var checksum = ComputeChecksum(payload);
        var plaintext = Format(payload, checksum);
        var hash = HashKey(plaintext);
        return new GeneratedActivationKey(plaintext, hash, plaintext[..PrefixLength]);
    }

    /// <summary>
    /// Verifies a raw plaintext key against a stored <see cref="ActivationKey"/>'s Argon2id hash.
    /// Malformed input never matches.
    /// </summary>
    public bool HashMatches(string rawKey, ActivationKey? storedKey)
    {
        if (storedKey is null)
        {
            return false;
        }

        return TryNormalize(rawKey, out var canonical) && VerifyHash(canonical, storedKey.KeyHash);
    }

    /// <summary>
    /// Returns true when the raw key is well-formed: brand, alphabet, length and checksum all valid.
    /// Pure and offline — no database access.
    /// </summary>
    public static bool IsWellFormed(string rawKey)
    {
        return TryNormalize(rawKey, out _);
    }

    /// <summary>
    /// Normalizes case and dash placement, then validates brand, alphabet, length and checksum.
    /// On success <paramref name="canonicalKey"/> is the canonical uppercase PCOPE-...-CC form.
    /// </summary>
    public static bool TryNormalize(string rawKey, out string canonicalKey)
    {
        canonicalKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        Span<char> normalized = stackalloc char[KeyLength];
        var count = 0;
        foreach (var ch in rawKey)
        {
            if (ch is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            if (count >= KeyLength)
            {
                return false;
            }

            normalized[count++] = char.ToUpperInvariant(ch);
        }

        if (count != KeyLength)
        {
            return false;
        }

        for (var i = 0; i < BrandLength; i++)
        {
            if (normalized[i] != Brand[i])
            {
                return false;
            }
        }

        for (var i = BrandLength; i < KeyLength; i++)
        {
            if (Base32Alphabet.IndexOf(normalized[i]) < 0)
            {
                return false;
            }
        }

        var payload = normalized.Slice(BrandLength, PayloadLength);
        var checksum = ComputeChecksum(payload);
        if (!checksum.AsSpan().SequenceEqual(normalized.Slice(BrandLength + PayloadLength, ChecksumLength)))
        {
            return false;
        }

        canonicalKey = Format(payload, checksum);
        return true;
    }

    public static bool IsExpired(ActivationKey key, DateTimeOffset utcNow)
    {
        return key.ExpiresAt is { } expiresAt && utcNow >= expiresAt;
    }

    public static bool IsRevoked(ActivationKey key)
    {
        return key.RevokedAt is not null;
    }

    public static bool IsExhausted(ActivationKey key)
    {
        return key.ActivationCount >= key.MaxActivations;
    }

    // 15 payload characters at 5 bits each = 75 bits, drawn from 10 CSPRNG bytes.
    private static string GeneratePayload()
    {
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);

        var chars = new char[PayloadLength];
        for (var i = 0; i < PayloadLength; i++)
        {
            var bitOffset = i * 5;
            var byteIndex = bitOffset / 8;
            var bitInByte = bitOffset % 8;
            var chunk = (random[byteIndex] << 8) | random[byteIndex + 1];
            var index = (chunk >> (11 - bitInByte)) & 0x1F;
            chars[i] = Base32Alphabet[index];
        }

        return new string(chars);
    }

    // Checksum: CRC-16/CCITT (poly 0x1021, init 0xFFFF) over the ASCII payload bytes, low 10 bits
    // encoded as two base32 characters. Independently recomputable client-side, no DB round trip.
    private static ushort ComputeCrc16(ReadOnlySpan<char> payload)
    {
        var crc = (ushort)0xFFFF;
        foreach (var ch in payload)
        {
            crc ^= (ushort)(ch << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        return crc;
    }

    private static string ComputeChecksum(ReadOnlySpan<char> payload)
    {
        var low = ComputeCrc16(payload) & 0x03FF;
        return new string(new[] { Base32Alphabet[(low >> 5) & 0x1F], Base32Alphabet[low & 0x1F] });
    }

    private static string Format(ReadOnlySpan<char> payload, string checksum)
    {
        return $"{Brand}-{new string(payload[..5])}-{new string(payload[5..10])}-{new string(payload[10..])}-{checksum}";
    }

    private static string HashKey(string canonicalKey)
    {
        var salt = RandomNumberGenerator.GetBytes(Argon2SaltSize);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(canonicalKey))
        {
            Salt = salt,
            MemorySize = Argon2MemorySizeKiB,
            Iterations = Argon2Iterations,
            DegreeOfParallelism = Argon2Parallelism
        };
        var hash = argon2.GetBytes(Argon2HashSize);
        return FormatHash(salt, hash, Argon2MemorySizeKiB, Argon2Iterations, Argon2Parallelism);
    }

    private static bool VerifyHash(string canonicalKey, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id" || !parts[1].StartsWith("v=", StringComparison.Ordinal))
        {
            return false;
        }

        var memorySize = 0;
        var iterations = 0;
        var parallelism = 0;
        foreach (var parameter in parts[2].Split(','))
        {
            var kv = parameter.Split('=');
            if (kv.Length != 2)
            {
                return false;
            }

            if (kv[0] == "m" && int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out memorySize))
            {
                continue;
            }

            if (kv[0] == "t" && int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations))
            {
                continue;
            }

            if (kv[0] == "p" && int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out parallelism))
            {
                continue;
            }

            return false;
        }

        if (memorySize <= 0 || iterations <= 0 || parallelism <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(canonicalKey))
        {
            Salt = salt,
            MemorySize = memorySize,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        var actual = argon2.GetBytes(expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string FormatHash(byte[] salt, byte[] hash, int memorySize, int iterations, int parallelism)
    {
        return $"argon2id$v=19$m={memorySize},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
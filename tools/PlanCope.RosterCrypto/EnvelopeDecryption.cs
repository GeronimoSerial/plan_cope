using System.Security.Cryptography;
using System.Text;

namespace PlanCope.RosterCrypto;

public static class EnvelopeDecryption
{
    public static async Task<byte[]> DecryptCueAsync(string bundlePath, string cue, string passphrase, CancellationToken cancellationToken = default)
    {
        var cueBytes = RosterBundleFormat.CueBytes(cue);
        await using var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Span<byte> magic = stackalloc byte[RosterBundleFormat.Magic.Length];
        RosterBundleFormat.ReadExactly(reader, magic);
        if (!magic.SequenceEqual(RosterBundleFormat.Magic) || reader.ReadUInt16() != RosterBundleFormat.Version)
        {
            throw new InvalidDataException("Unsupported roster bundle header.");
        }

        var parameters = new Argon2Parameters(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        parameters.Validate();
        var salt = reader.ReadBytes(RosterBundleFormat.SaltSize);
        if (salt.Length != RosterBundleFormat.SaltSize) throw new InvalidDataException("Roster bundle is truncated.");
        var entryCount = reader.ReadInt32();
        if (entryCount < 0 || entryCount > 100_000) throw new InvalidDataException("Roster bundle entry count is invalid.");

        var masterKey = RosterBundleFormat.DeriveMasterKey(passphrase, salt, parameters);
        try
        {
            for (var index = 0; index < entryCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var storedCue = reader.ReadBytes(RosterBundleFormat.CueSize);
                var dataNonce = reader.ReadBytes(RosterBundleFormat.NonceSize);
                var dataTag = reader.ReadBytes(RosterBundleFormat.TagSize);
                var wrapNonce = reader.ReadBytes(RosterBundleFormat.NonceSize);
                var wrapTag = reader.ReadBytes(RosterBundleFormat.TagSize);
                var wrappedDek = reader.ReadBytes(RosterBundleFormat.KeySize);
                var checksum = reader.ReadBytes(RosterBundleFormat.ChecksumSize);
                if (storedCue.Length != RosterBundleFormat.CueSize || dataNonce.Length != RosterBundleFormat.NonceSize ||
                    dataTag.Length != RosterBundleFormat.TagSize || wrapNonce.Length != RosterBundleFormat.NonceSize ||
                    wrapTag.Length != RosterBundleFormat.TagSize || wrappedDek.Length != RosterBundleFormat.KeySize ||
                    checksum.Length != RosterBundleFormat.ChecksumSize)
                    throw new InvalidDataException("Roster bundle is truncated.");
                var ciphertextLength = reader.ReadInt32();
                if (ciphertextLength < 0 || ciphertextLength > stream.Length - stream.Position)
                    throw new InvalidDataException("Roster bundle entry length is invalid.");
                if (!storedCue.AsSpan().SequenceEqual(cueBytes))
                {
                    stream.Seek(ciphertextLength, SeekOrigin.Current);
                    continue;
                }

                var ciphertext = new byte[ciphertextLength];
                await stream.ReadExactlyAsync(ciphertext, cancellationToken);
                var aad = RosterBundleFormat.EntryAssociatedData(storedCue, checksum);
                var dek = new byte[RosterBundleFormat.KeySize];
                try
                {
                    using (var wrapper = new AesGcm(masterKey, RosterBundleFormat.TagSize))
                        wrapper.Decrypt(wrapNonce, wrappedDek, wrapTag, dek, aad);
                    var plaintext = new byte[ciphertextLength];
                    try
                    {
                        using var cipher = new AesGcm(dek, RosterBundleFormat.TagSize);
                        cipher.Decrypt(dataNonce, ciphertext, dataTag, plaintext, aad);
                        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(plaintext), checksum))
                            throw new CryptographicException("Roster entry checksum mismatch.");
                        return plaintext;
                    }
                    catch
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                        throw;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(dek);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
        throw new RosterEntryNotFoundException(cue);
    }
}

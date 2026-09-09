using System.Security.Cryptography;
using System.Text.Json;

namespace PlanCope.RosterCrypto;

public static class EnvelopeEncryption
{
    public static async Task<RosterBundleManifest> EncryptDirectoryAsync(
        string inputDirectory,
        string outputPath,
        string passphrase,
        Argon2Parameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        parameters ??= new Argon2Parameters();
        parameters.Validate();

        var files = Directory.EnumerateFiles(inputDirectory, "*.roster.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The input directory contains no .roster.json files.");
        }
        var duplicateCue = files.Select(Path.GetFileName)
            .Where(static name => name is not null && name.Length >= RosterBundleFormat.CueSize)
            .Select(static name => name![..RosterBundleFormat.CueSize])
            .GroupBy(static cue => cue, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateCue is not null)
        {
            throw new InvalidDataException($"The input directory contains multiple rosters for CUE '{duplicateCue.Key}'.");
        }

        var salt = RandomNumberGenerator.GetBytes(RosterBundleFormat.SaltSize);
        var masterKey = RosterBundleFormat.DeriveMasterKey(passphrase, salt, parameters);
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        var manifestEntries = new List<RosterBundleManifestEntry>(files.Length);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(RosterBundleFormat.Magic);
            writer.Write(RosterBundleFormat.Version);
            writer.Write(parameters.MemorySizeKiB);
            writer.Write(parameters.Iterations);
            writer.Write(parameters.Parallelism);
            writer.Write(salt);
            writer.Write(files.Length);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                var separator = name.IndexOf('-');
                if (separator != RosterBundleFormat.CueSize)
                {
                    throw new InvalidDataException($"Roster file '{name}' must start with a 9-digit CUE followed by '-'.");
                }
                var cueBytes = RosterBundleFormat.CueBytes(name[..separator]);
                var plaintext = await File.ReadAllBytesAsync(file, cancellationToken);
                var checksum = SHA256.HashData(plaintext);
                var aad = RosterBundleFormat.EntryAssociatedData(cueBytes, checksum);
                var dek = RandomNumberGenerator.GetBytes(RosterBundleFormat.KeySize);
                var dataNonce = RandomNumberGenerator.GetBytes(RosterBundleFormat.NonceSize);
                var dataTag = new byte[RosterBundleFormat.TagSize];
                var ciphertext = new byte[plaintext.Length];
                using (var cipher = new AesGcm(dek, RosterBundleFormat.TagSize))
                {
                    cipher.Encrypt(dataNonce, plaintext, ciphertext, dataTag, aad);
                }
                var wrapNonce = RandomNumberGenerator.GetBytes(RosterBundleFormat.NonceSize);
                var wrapTag = new byte[RosterBundleFormat.TagSize];
                var wrappedDek = new byte[RosterBundleFormat.KeySize];
                using (var wrapper = new AesGcm(masterKey, RosterBundleFormat.TagSize))
                {
                    wrapper.Encrypt(wrapNonce, dek, wrappedDek, wrapTag, aad);
                }

                var offset = stream.Position;
                writer.Write(cueBytes);
                writer.Write(dataNonce);
                writer.Write(dataTag);
                writer.Write(wrapNonce);
                writer.Write(wrapTag);
                writer.Write(wrappedDek);
                writer.Write(checksum);
                writer.Write(ciphertext.Length);
                writer.Write(ciphertext);
                manifestEntries.Add(new(name[..separator], offset, ciphertext.Length, Convert.ToHexString(checksum).ToLowerInvariant()));
                CryptographicOperations.ZeroMemory(dek);
                CryptographicOperations.ZeroMemory(plaintext);
            }
            await stream.FlushAsync(cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return new(RosterBundleFormat.Version, parameters, manifestEntries);
    }

    public static async Task WriteManifestAsync(RosterBundleManifest manifest, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}

using System.Security.Cryptography;
using System.Runtime.Versioning;

namespace PlanCope.Local.Host.Services;

[SupportedOSPlatform("windows")]
public sealed class ActivationKeyStore(DataDirectoryResolver directories)
{
    private static readonly byte[] OptionalEntropy = "PlanCope.Local.Activation.v1"u8.ToArray();
    private string KeyPath => Path.Combine(directories.ConfigDirectory, "activation.key");

    public bool HasStoredKey => File.Exists(KeyPath);

    public void Store(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("La clave de activacion no puede estar vacia.", nameof(key));
        }

        var protectedBytes = ProtectedData.Protect(key, OptionalEntropy, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{KeyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, KeyPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public byte[] Load()
    {
        if (!HasStoredKey)
        {
            throw new InvalidOperationException("La maquina todavia no fue activada.");
        }

        return ProtectedData.Unprotect(
            File.ReadAllBytes(KeyPath),
            OptionalEntropy,
            DataProtectionScope.CurrentUser);
    }
}

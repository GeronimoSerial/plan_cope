using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

// The DPAPI round-trip (Store/Load over real ProtectedData) is untestable on this non-Windows host and needs real Windows; these tests cover only guard clauses and preconditions.
#pragma warning disable CA1416 // ActivationKeyStore is [SupportedOSPlatform("windows")]; this test project stays plain net8.0 so it keeps building on any OS, mirroring LocalDataServiceCollectionExtensions.
public sealed class ActivationKeyStoreTests
{
    [Fact]
    public void HasStoredKey_is_false_before_any_store_and_does_not_touch_disk_beyond_the_directories()
    {
        using var tempRoot = new TempDataRoot();
        var resolver = new DataDirectoryResolver(tempRoot.Root);
        var store = new ActivationKeyStore(resolver);

        Assert.False(store.HasStoredKey);
        Assert.False(File.Exists(Path.Combine(resolver.ConfigDirectory, "activation.key")));
    }

    [Fact]
    public void Store_rejects_null_key()
    {
        using var tempRoot = new TempDataRoot();
        var store = new ActivationKeyStore(new DataDirectoryResolver(tempRoot.Root));

        Assert.Throws<ArgumentNullException>(() => store.Store(null!));
    }

    [Fact]
    public void Store_rejects_empty_key()
    {
        using var tempRoot = new TempDataRoot();
        var store = new ActivationKeyStore(new DataDirectoryResolver(tempRoot.Root));

        var ex = Assert.Throws<ArgumentException>(() => store.Store(Array.Empty<byte>()));

        Assert.StartsWith("La clave de activacion no puede estar vacia.", ex.Message);
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void Load_throws_when_no_key_was_ever_stored()
    {
        using var tempRoot = new TempDataRoot();
        var store = new ActivationKeyStore(new DataDirectoryResolver(tempRoot.Root));

        var ex = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Equal("La maquina todavia no fue activada.", ex.Message);
    }

    private sealed class TempDataRoot : IDisposable
    {
        public TempDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "PlanCope.Tests.DataDir." + Guid.NewGuid().ToString("N"));
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
#pragma warning restore CA1416
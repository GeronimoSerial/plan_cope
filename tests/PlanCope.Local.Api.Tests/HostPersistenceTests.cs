using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class DataDirectoryResolverTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"plancope-directories-{Guid.NewGuid():N}");

    [Fact]
    public void DataDirectoryResolver_UsesOverrideAndCreatesExpectedStructure()
    {
        var resolver = new DataDirectoryResolver(tempRoot);

        Assert.Equal(Path.GetFullPath(tempRoot), resolver.RootDirectory);
        Assert.All(
            [resolver.DataDirectory, resolver.AssetsDirectory, resolver.ConfigDirectory, resolver.LogsDirectory],
            path => Assert.True(Directory.Exists(path)));
        Assert.Equal(Path.Combine(tempRoot, "data", "plan-cope-local.db"), resolver.DatabasePath);
    }

    [Fact]
    public void DataDirectoryResolver_HonorsEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable("PLANCOPE_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("PLANCOPE_DATA_DIR", tempRoot);
            Assert.Equal(Path.GetFullPath(tempRoot), new DataDirectoryResolver().RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLANCOPE_DATA_DIR", previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
    }
}

public sealed class LegacyDatabaseMigratorTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"plancope-migration-{Guid.NewGuid():N}");

    [Fact]
    public void DataMigration_CopiesLegacyDatabaseOnlyOnce()
    {
        var legacyDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "legacy")).FullName;
        var resolver = new DataDirectoryResolver(Path.Combine(tempRoot, "persistent"));
        var legacyPath = Path.Combine(legacyDirectory, "plan-cope-local.db");
        File.WriteAllText(legacyPath, "original database bytes");
        var migrator = new LegacyDatabaseMigrator(resolver);

        Assert.True(migrator.MigrateIfNeeded(legacyDirectory));
        Assert.Equal("original database bytes", File.ReadAllText(resolver.DatabasePath));

        File.WriteAllText(legacyPath, "new legacy bytes");
        Assert.False(migrator.MigrateIfNeeded(legacyDirectory));
        Assert.Equal("original database bytes", File.ReadAllText(resolver.DatabasePath));
    }

    [Fact]
    public void DataMigration_PreservesLegacyRowsWhenDatabaseInitializesAtNewLocation()
    {
        var legacyDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "startup-legacy")).FullName;
        var resolver = new DataDirectoryResolver(Path.Combine(tempRoot, "startup-persistent"));
        var legacyPath = Path.Combine(legacyDirectory, "plan-cope-local.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = legacyPath }.ToString();

        new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES ('proof-id', 'migration-proof', '\"kept\"', CURRENT_TIMESTAMP);";
            command.ExecuteNonQuery();
        }

        Assert.True(new LegacyDatabaseMigrator(resolver).MigrateIfNeeded(legacyDirectory));
        var migratedConnectionString = new SqliteConnectionStringBuilder { DataSource = resolver.DatabasePath }.ToString();
        new LocalDatabaseInitializer(new LocalDatabaseOptions(migratedConnectionString)).Initialize();

        using var migratedConnection = new SqliteConnection(migratedConnectionString);
        migratedConnection.Open();
        using var migratedCommand = migratedConnection.CreateCommand();
        migratedCommand.CommandText = "SELECT value_json FROM sync_state WHERE key = 'migration-proof';";
        Assert.Equal("\"kept\"", migratedCommand.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
    }
}

public sealed class ActivationKeyStoreTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"plancope-activation-{Guid.NewGuid():N}");

    [Fact]
    public void ActivationKeyStore_ProtectsAndUnprotectsBytesForCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new ActivationKeyStore(new DataDirectoryResolver(tempRoot));
        var key = new byte[] { 2, 3, 5, 7, 11, 13 };

        store.Store(key);

        Assert.True(store.HasStoredKey);
        Assert.Equal(key, store.Load());
        Assert.NotEqual(key, File.ReadAllBytes(Path.Combine(tempRoot, "config", "activation.key")));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
    }
}

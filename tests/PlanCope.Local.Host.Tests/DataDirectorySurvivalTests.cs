using System.Reflection;
using System.Security.Cryptography;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

/// <summary>
/// Automates the testable core of docs/velopack-test-matrix.md Scenario 3
/// ("Persistent data survives the update", lines 64-85): the checksum-comparison
/// guarantee around the resolved data directory, minus the OS-level install/update
/// cycle that the doc itself marks as "must be run by hand" on a real Windows machine.
///
/// What these tests DO prove:
///   - DataDirectoryResolver under an explicit temp root yields a stable, well-known
///     layout (data/assets/config/logs) that the checksum harness can enumerate.
///   - The checksum-manifest harness is deterministic: recomputing with nothing in
///     between produces an identical manifest (a stability check on the harness the
///     manual Scenario 3 steps rely on, so a Windows tester's before/after diff is
///     trustworthy).
///   - The in-process update path (UpdateService, VelopackUpdateBackend) contains no
///     reference to the resolver or to the root it derives from, at both the compiled
///     (reflection) and source level. The only route an updater has into the data
///     directory is Velopack's own native installer behavior.
///
/// What these tests CANNOT prove (stays manual, out of scope):
///   A real `vpk`-produced update install/apply cycle leaves the resolved directory
///   byte-identical. That requires Velopack's OS-level installer on Windows
///   (Scenario 3 steps 2-4). There is no automated proxy that exercises that code
///   path honestly on this (non-Windows) CI host — constructing a
///   VelopackUpdateBackend here and calling ApplyUpdatesAndRestart() would throw or
///   exit on a platform without the Windows installer, so a test that "ran an
///   update" would be fake. Closing the gap means running Scenario 3 by hand and
///   diffing real checksums, exactly as the doc requires.
/// </summary>
public sealed class DataDirectorySurvivalTests
{
    [Fact]
    public void Resolver_WithExplicitTempRoot_CreatesExpectedLayout()
    {
        using var tempRoot = new TempDataRoot();
        var resolver = new DataDirectoryResolver(tempRoot.Root);

        Assert.Equal(tempRoot.Root, resolver.RootDirectory);
        Assert.True(Directory.Exists(resolver.DataDirectory));
        Assert.True(Directory.Exists(resolver.AssetsDirectory));
        Assert.True(Directory.Exists(resolver.ConfigDirectory));
        Assert.True(Directory.Exists(resolver.LogsDirectory));
        Assert.StartsWith(resolver.DataDirectory + Path.DirectorySeparatorChar, resolver.DatabasePath);
    }

    [Fact]
    public void ChecksumManifest_RecomputedWithNoInterveningWrites_IsIdentical()
    {
        using var tempRoot = new TempDataRoot();
        var resolver = new DataDirectoryResolver(tempRoot.Root);

        WriteSyntheticFiles(resolver);

        var first = ComputeChecksumManifest(resolver);
        var second = ComputeChecksumManifest(resolver);

        Assert.Equal(first, second);
    }

    [Fact]
    public void UpdatePathCode_DoesNotReferenceResolvedDataDirectory()
    {
        // Compiled-level assertion: the update orchestration types (source-linked into
        // this assembly) declare no field, property, parameter, or return type that
        // reaches DataDirectoryResolver. If updater code ever gained a handle on the
        // resolved data directory, this fails.
        foreach (var type in new[] { typeof(UpdateService), typeof(VelopackUpdateBackend) })
        {
            Assert.DoesNotContain(typeof(DataDirectoryResolver), GetReferencedTypes(type));
        }

        // Source-level assertion: the update-path source never names the resolver, its
        // env-var override, or the %LOCALAPPDATA% root it derives from. Re-deriving the
        // data root inside updater code would be exactly the kind of path drift
        // Scenario 3 step 4 is written to catch.
        var source = ReadLinkedUpdateSource();
        Assert.DoesNotContain("DataDirectoryResolver", source);
        Assert.DoesNotContain("PLANCOPE_DATA_DIR", source);
        Assert.DoesNotContain("LocalApplicationData", source);
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            yield return method.ReturnType;
        }
    }

    private static string ReadLinkedUpdateSource()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Local",
            "PlanCope.Local.Host",
            "Services",
            "UpdateService.cs");

        Assert.True(File.Exists(sourcePath), $"UpdateService.cs not found at '{sourcePath}'.");

        return File.ReadAllText(sourcePath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "PlanCope.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }

    private static void WriteSyntheticFiles(DataDirectoryResolver resolver)
    {
        // Synthetic content only — never the real padrón, per Scenario 3's own rule.
        File.WriteAllText(
            Path.Combine(resolver.DataDirectory, "plan-cope-local.db"),
            "synthetic sqlite page blob -- not real roster data");
        File.WriteAllText(
            Path.Combine(resolver.DataDirectory, "roster-snapshot.json"),
            """{"students":[{"id":"SYN-001","fullName":"Synthetic Student"}],"source":"checksum-survival-test"}""");
        File.WriteAllText(
            Path.Combine(resolver.ConfigDirectory, "appsettings.local.json"),
            """{"theme":"dark","updateChannel":"stable"}""");
        File.WriteAllText(
            Path.Combine(resolver.ConfigDirectory, "feature-flags.json"),
            """{"gatedUpdates":true}""");
        File.WriteAllBytes(
            Path.Combine(resolver.AssetsDirectory, "logo.png"),
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D });
        File.WriteAllBytes(
            Path.Combine(resolver.AssetsDirectory, "seed.bin"),
            Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
    }

    private static string ComputeChecksumManifest(DataDirectoryResolver resolver)
    {
        // Mirrors the doc's "Get-ChildItem -Recurse | Get-FileHash" over the data,
        // assets, and config subdirectories of the resolved root.
        var lines = new List<string>();

        foreach (var directory in new[]
                 {
                     resolver.DataDirectory,
                     resolver.AssetsDirectory,
                     resolver.ConfigDirectory,
                 })
        {
            foreach (var file in Directory
                         .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                lines.Add($"{Path.GetRelativePath(resolver.RootDirectory, file)}|{hash}");
            }
        }

        return string.Join("\n", lines);
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
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The spec-dependency load-failure mappings, unit-tested without a workspace. An assembly-load
///     <see cref="FileNotFoundException" /> (its FileName carries a full assembly identity) becomes a
///     pinned three-line actionable <see cref="UserErrorException" /> with the original attached, and so
///     does a <see cref="TypeLoadException" /> from a type whose closure leaves the .NET surface; a
///     FileName that is a plain path or absent is not treated as an assembly-load failure.
/// </summary>
/// <remarks>
///     <para>
///         The exceptions here are fabricated, which pins the message text. The runtime behaviour behind
///         each arm — a net48 spec really failing this way in the collectible ALC, a net10 spec really
///         failing on a shared-framework anchor — is pinned separately by the fixture tests.
///     </para>
///     <para>
///         Three of the four assembly cases differ only in what sits beside the spec DLL, which is the
///         point: the remedy is chosen by reading the spec's own <c>.deps.json</c>, so the manifest is the
///         only variable each case moves.
///     </para>
/// </remarks>
public sealed class SpecDependencyLoadFailureTests
{
    private const string MissingAssembly =
        "Microsoft.Data.SqlClient, Version=7.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5";

    private const string Opening =
        "The spec assembly 'Meridian.ArchSpec' failed to load its dependency "
        + "'Microsoft.Data.SqlClient, Version=7.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5' "
        + "while running Define().\n";

    [Fact]
    public void Map_ManifestNamingTheAssembly_OffersTheStagingRemedy()
    {
        // Arrange: the manifest declares the assembly as a package runtime asset, so it resolved at build
        // time and is simply not where the loader can reach it now.
        using TempDirectory output = TestTempRoot.Fresh("spec-load-failure");
        string specDllPath = WriteManifest(output, ManifestNamingSqlClient);
        var exception = new FileNotFoundException("Could not load file or assembly ...", MissingAssembly);

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, specDllPath);

        // Assert
        result.Message.ShouldBe(
            Opening +
            "A typeof() anchor loads its type's assembly, and the spec's own dependency manifest (.deps.json) names this one — so it is a package asset that resolved when the spec was built, and it is now neither staged beside the spec DLL nor present in this machine's NuGet package folder.\n" +
            "Add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild, so the assembly is staged beside the spec.");
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Map_ManifestWithoutTheAssembly_WithholdsTheStagingRemedy()
    {
        // Arrange: a well-formed manifest that simply does not mention the assembly — a <FrameworkReference>
        // contributes nothing to one, which is why the staging remedy cannot work here.
        using TempDirectory output = TestTempRoot.Fresh("spec-load-failure");
        string specDllPath = WriteManifest(output, ManifestWithoutSqlClient);
        var exception = new FileNotFoundException("Could not load file or assembly ...", MissingAssembly);

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, specDllPath);

        // Assert
        result.Message.ShouldBe(Opening + NoBuildSettingRemedy);
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Map_NoManifestBesideTheSpec_TakesTheSameNoBuildSettingBranch()
    {
        // Arrange: what a .NET Framework spec's output looks like — no .deps.json at all. Its absence is
        // an answer rather than an unknown: a spec with no manifest declares no dependency to stage.
        using TempDirectory output = TestTempRoot.Fresh("spec-load-failure");
        string specDllPath = output.PathOf("Meridian.ArchSpec.dll");
        var exception = new FileNotFoundException("Could not load file or assembly ...", MissingAssembly);

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, specDllPath);

        // Assert
        result.Message.ShouldBe(Opening + NoBuildSettingRemedy);
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Map_UnreadableManifest_FallsBackToTheBothWorldsWording()
    {
        // Arrange: malformed JSON stands for every way reading a manifest can fail. A diagnostic that threw
        // while explaining a failure would replace the user's problem with ours, so the branch degrades to
        // the wording that names both worlds and lets the reader choose.
        using TempDirectory output = TestTempRoot.Fresh("spec-load-failure");
        string specDllPath = WriteManifest(output, "{ not json");
        var exception = new FileNotFoundException("Could not load file or assembly ...", MissingAssembly);

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, specDllPath);

        // Assert
        result.Message.ShouldBe(
            Opening +
            "A typeof() anchor loads its type's assembly, and this one is not beside the spec DLL: a class-library build does not stage NuGet package assemblies into its output, and a .NET Framework reference assembly resolves from the targeting pack or the GAC and is never staged at all.\n" +
            "If it is a package, add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild; if it is a .NET Framework assembly, no build setting can help — target the type with a namespace pattern (arch.Namespace(...)), which needs no assembly load.");
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Map_TypeLoadExceptionWithoutTypeName_DegradesToTheUnnamedForm()
    {
        // Arrange: TypeLoadException.TypeName is set only by the runtime — no public constructor reaches
        // it — so a fabricated one exercises the degraded branch. The named form is pinned by
        // LegacySpecLoadingTests against a real Framework-only interface.
        var exception = new TypeLoadException("Could not load type ...");

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, "Legacy.ArchSpec.dll");

        // Assert
        result.Message.ShouldBe(
            "The spec assembly 'Legacy.ArchSpec' could not load a type while running Define().\n" +
            "A typeof() anchor loads its type's whole closure, base types and implemented interfaces included, and this one reaches an assembly that does not exist on .NET — so the anchor cannot resolve however the spec project is built.\n" +
            "Target the type with a namespace pattern (arch.Namespace(...)) instead, which needs no assembly load.");
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void IsAssemblyLoadFailure_PathFileName_IsFalse()
    {
        var exception = new FileNotFoundException("File not found.", "config/settings.json");

        SpecDependencyLoadFailure.IsAssemblyLoadFailure(exception)
            .ShouldBeFalse();
    }

    [Fact]
    public void IsAssemblyLoadFailure_NullFileName_IsFalse()
    {
        var exception = new FileNotFoundException("File not found.");

        SpecDependencyLoadFailure.IsAssemblyLoadFailure(exception)
            .ShouldBeFalse();
    }

    /// <summary>
    ///     Writes <paramref name="manifest" /> as the <c>.deps.json</c> beside a spec DLL path, and returns
    ///     that path. The DLL itself is never written: the mapping reads the manifest and nothing else.
    /// </summary>
    private static string WriteManifest(TempDirectory output, string manifest)
    {
        string specDllPath = output.PathOf("Meridian.ArchSpec.dll");
        File.WriteAllText(output.PathOf("Meridian.ArchSpec.deps.json"), manifest);
        return specDllPath;
    }

    /// <summary>The remedy for an assembly no build setting can stage — one branch, reached two ways.</summary>
    private const string NoBuildSettingRemedy =
        "A typeof() anchor loads its type's assembly, and this one is not among the spec's own dependencies: the spec's dependency manifest (.deps.json) does not name it, and a .NET Framework spec has no manifest at all. No build setting stages it — it comes from a .NET shared framework pulled in by <FrameworkReference> (Microsoft.AspNetCore.App, Microsoft.WindowsDesktop.App) that this tool's host does not carry, or from a .NET Framework targeting pack or the GAC. CopyLocalLockFileAssemblies has nothing to copy in either case.\n" +
        "Anchor the type by name instead of by typeof(): every typeof() anchor position carries a string overload — .DerivedFrom(\"Microsoft.AspNetCore.Mvc.ControllerBase\") renders identically to the typeof() form — and a string anchor needs no assembly load, as does a namespace pattern (arch.Namespace(...)).";

    /// <summary>
    ///     A real manifest's shape, trimmed: the spec's own entry plus one package contributing a runtime
    ///     asset. The asset key is the package-relative path, which is what carries the assembly's name.
    /// </summary>
    private const string ManifestNamingSqlClient =
        """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
          "compilationOptions": {},
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "Meridian.ArchSpec/1.0.0": {
                "dependencies": { "Microsoft.Data.SqlClient": "7.0.0" },
                "runtime": { "Meridian.ArchSpec.dll": {} }
              },
              "Microsoft.Data.SqlClient/7.0.0": {
                "runtime": {
                  "lib/net9.0/Microsoft.Data.SqlClient.dll": {
                    "assemblyVersion": "7.0.0.0",
                    "fileVersion": "7.0.0.0"
                  }
                }
              }
            }
          },
          "libraries": {
            "Meridian.ArchSpec/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
            "Microsoft.Data.SqlClient/7.0.0": { "type": "package", "serviceable": true, "sha512": "" }
          }
        }
        """;

    /// <summary>
    ///     The same manifest with the package gone — the shape a <c>&lt;FrameworkReference&gt;</c> produces,
    ///     which contributes no entry however the spec project sets <c>CopyLocalLockFileAssemblies</c>.
    /// </summary>
    private const string ManifestWithoutSqlClient =
        """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
          "compilationOptions": {},
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "Meridian.ArchSpec/1.0.0": {
                "runtime": { "Meridian.ArchSpec.dll": {} }
              }
            }
          },
          "libraries": {
            "Meridian.ArchSpec/1.0.0": { "type": "project", "serviceable": false, "sha512": "" }
          }
        }
        """;
}

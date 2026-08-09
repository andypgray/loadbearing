using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The spec-dependency load-failure mappings, unit-tested without a workspace. An assembly-load
///     <see cref="FileNotFoundException" /> (its FileName carries a full assembly identity) becomes the
///     pinned three-line actionable <see cref="UserErrorException" /> with the original attached, and so
///     does a <see cref="TypeLoadException" /> from a type whose closure leaves the .NET surface; a
///     FileName that is a plain path or absent is not treated as an assembly-load failure.
/// </summary>
/// <remarks>
///     The exceptions here are fabricated, which pins the message text. The runtime behaviour behind each
///     arm — a net48 spec really failing this way in the collectible ALC — is pinned separately by the
///     legacy fixture tests.
/// </remarks>
public sealed class SpecDependencyLoadFailureTests
{
    [Fact]
    public void Map_AssemblyIdentityFileName_PinsActionableMessage()
    {
        // Arrange
        var exception = new FileNotFoundException(
            "Could not load file or assembly ...",
            "Microsoft.Data.SqlClient, Version=7.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5");

        // Act
        UserErrorException result = SpecDependencyLoadFailure.Map(exception, "Meridian.ArchSpec.dll");

        // Assert: the remedy line covers both worlds. A .NET Framework spec reaches this arm too (a
        // framework reference assembly is never staged into bin), and for it CopyLocalLockFileAssemblies
        // is a dead end — so the packaging fix is offered conditionally, not as the first answer.
        result.Message.ShouldBe(
            "The spec assembly 'Meridian.ArchSpec' failed to load its dependency 'Microsoft.Data.SqlClient, Version=7.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5' while running Define().\n" +
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
}

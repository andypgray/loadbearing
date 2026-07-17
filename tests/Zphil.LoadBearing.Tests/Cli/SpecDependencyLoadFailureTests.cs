using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Finding F1's papercut mapping, unit-tested without a workspace: an assembly-load
///     <see cref="FileNotFoundException" /> (its FileName carries a full assembly identity) becomes the
///     pinned three-line actionable <see cref="UserErrorException" /> with the original attached, while a
///     FileName that is a plain path or absent is not treated as an assembly-load failure.
/// </summary>
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

        // Assert
        result.Message.ShouldBe(
            "The spec assembly 'Meridian.ArchSpec' failed to load its dependency 'Microsoft.Data.SqlClient, Version=7.0.0.0, Culture=neutral, PublicKeyToken=23ec7fc2d6eaa4a5' while running Define().\n" +
            "A class-library build does not stage NuGet package assemblies into its output, so a spec that names a packaged type via typeof() builds clean but cannot run.\n" +
            "Add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to the spec .csproj and rebuild, or target the type with a namespace pattern (arch.Namespace(...)), which needs no assembly load.");
        result.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void IsAssemblyLoadFailure_PathFileName_IsFalse()
    {
        var exception = new FileNotFoundException("File not found.", "config/settings.json");

        SpecDependencyLoadFailure.IsAssemblyLoadFailure(exception).ShouldBeFalse();
    }

    [Fact]
    public void IsAssemblyLoadFailure_NullFileName_IsFalse()
    {
        var exception = new FileNotFoundException("File not found.");

        SpecDependencyLoadFailure.IsAssemblyLoadFailure(exception).ShouldBeFalse();
    }
}
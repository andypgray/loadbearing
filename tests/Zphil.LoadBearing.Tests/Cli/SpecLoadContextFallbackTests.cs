using System.Reflection;
using System.Runtime.Loader;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <see cref="SpecLoadContext.LoadWithoutLocking" />'s missing-file arm, which every other spec-load test
///     walks past. The happy path reads bytes so nothing is locked; the arm hands the path to the loader
///     instead, and until these tests nothing said when it is reached or what it produces.
/// </summary>
/// <remarks>
///     <para>
///         <b>The arm is not on the missing-dependency route.</b>
///         <see cref="AssemblyDependencyResolver" /> re-checks the disk before returning a path, so a
///         dependency that has gone resolves to null, <see cref="AssemblyLoadContext.Load" /> returns null,
///         and the spec's own directory stops being consulted at all — whatever happens next belongs to the
///         Default context. The arm is reached only when the spec DLL named on the command line is itself
///         gone by the time it is loaded, and there the failure names the path, because a path is all the
///         loader was given. Both facts are the ordinary shape of a rebuild landing underneath a running
///         host, which the long-lived MCP server is.
///     </para>
///     <para>
///         <b>What is deliberately not tested here.</b> That a vanished dependency still fails with the
///         assembly display identity <c>SpecDependencyLoadFailure</c> keys on is pinned end to end by
///         <see cref="ModelPipelineSpecLoadFailureTests" /> against a net48 fixture. It cannot be pinned
///         against a net10 one: once the resolver returns null the outcome is the Default context's, and
///         whether Default can supply <c>MyApp.Legacy.Billing</c> depends on whether anything else in the
///         run has loaded it — <see cref="Oracle.OracleArchitecture" /> does, with
///         <see cref="Assembly.LoadFrom(string)" />. Such a test passes alone and passes or fails in a suite
///         according to what ran first, which is not a property of this system.
///     </para>
/// </remarks>
public sealed class SpecLoadContextFallbackTests
{
    private const string DependencyDll = "MyApp.Legacy.Billing.dll";

    [Fact]
    public void ResolveAssemblyToPath_WhenTheDependencyIsDeletedAfterConstruction_ReturnsNull()
    {
        // Arrange: an intact private copy of the net10 layer spec, whose .deps.json lists the dependency.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.LayerSpecDll);
        var resolver = new AssemblyDependencyResolver(stagedSpec);
        var dependency = new AssemblyName(Path.GetFileNameWithoutExtension(DependencyDll));

        // Act: the manifest is read once at construction, so deletion is the only thing that changes.
        string? beforeDeletion = resolver.ResolveAssemblyToPath(dependency);
        File.Delete(Path.Combine(Path.GetDirectoryName(stagedSpec)!, DependencyDll));
        string? afterDeletion = resolver.ResolveAssemblyToPath(dependency);

        // Assert: a path it once produced it will not produce again. This is why the missing-file arm cannot
        // be reached through a missing dependency, and why the arm's remarks say what they say.
        beforeDeletion.ShouldNotBeNull();
        afterDeletion.ShouldBeNull(
            "the resolver handed back a path that is no longer on disk; SpecLoadContext's missing-file arm "
            + "would then be reachable from Load(), and what it produces there would need pinning.");
    }

    [Fact]
    public void LoadWithoutLocking_WhenTheSpecItselfIsGone_ThrowsTheLoaderFailureNamingThePath()
    {
        // Arrange: the one route that reaches the arm — the spec DLL named on the command line resolved, and
        // then vanished before it was loaded.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.LayerSpecDll);
        var context = new SpecLoadContext(stagedSpec);
        try
        {
            File.Delete(stagedSpec);

            // Act
            var thrown = Should.Throw<FileNotFoundException>(() => context.LoadWithoutLocking(stagedSpec));

            // Assert: a path is what the loader reports, so this is not a dependency failure and must not be
            // reported as one — the packaging remedy would be nonsense for a file the user has not built.
            thrown.FileName.ShouldBe(stagedSpec);
            SpecDependencyLoadFailure.IsAssemblyLoadFailure(thrown)
                .ShouldBeFalse(
                    "a missing spec DLL is not a missing dependency; reporting it as one would offer the "
                    + "CopyLocalLockFileAssemblies remedy for a file that was simply never built.");
        }
        finally
        {
            context.Unload();
        }
    }
}

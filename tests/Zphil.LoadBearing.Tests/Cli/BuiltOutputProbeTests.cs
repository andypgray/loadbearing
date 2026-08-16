using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The built-output search over hostile build trees, hand-built in temp directories — no MSBuild, no
///     fixture solutions, zero-byte assemblies. Two of these trees are field layouts the skew matrix measured
///     refusing a fully built solution (a parent props file's flat <c>OutputPath</c>, and
///     <c>UseArtifactsOutput</c> built in one configuration); the rest pin the anchor rule, the two ranking
///     keys, the exclusions, the intermediate-assembly refusal, and the narrowing that pays for all of it.
/// </summary>
/// <remarks>
///     <para>
///         The intermediate-root derivation the refusal rests on is pinned by
///         <c>Roslyn/IntermediateOutputTreeTests</c>, beside the type that owns it — the cache probes derive
///         the assets file's location from the same primitive, and a rule two subsystems depend on should not
///         be pinned only through one of them.
///     </para>
///     <para>
///         Every tree here is fabricated <em>after</em> any load, so one axis sits structurally outside this
///         class: the design-time build <c>check</c>'s own workspace load runs creates the evaluated output
///         directory — empty — before resolution reads the disk. Both field layouts are covered against a
///         real SDK restore, a real <c>-c Release</c> build and a live workspace load by
///         <c>Cli/OutputLayoutSpecResolutionE2ETests</c>, which is where that axis is measured rather than
///         described.
///     </para>
/// </remarks>
public sealed class BuiltOutputProbeTests
{
    private const string Assembly = "MyApp.Arch.dll";

    // Distinct, explicit write times, so a ranking assertion can never be decided by file-system timestamp
    // granularity or by the order the arrange happened to create things in.
    private static readonly DateTime Older = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Find_FlatOutputPathFromAParentPropsFile_ResolvesTheConfigurationSubdirectory()
    {
        // The field layout: a parent Directory.Build.props carrying <OutputPath>bin\$(Configuration)\</OutputPath>.
        // A props file is imported before the SDK defaults Configuration, so the evaluated path is the flat
        // `bin\` while every real build lands one level down — and the whole solution was refused as unbuilt.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssembly(temp, "bin", "Debug", Assembly);
        string intermediate = WriteAssembly(temp, "obj", "Debug", Assembly);
        string evaluated = temp.PathOf("bin", Assembly);

        BuiltOutputProbe.Find(evaluated, intermediate)
            .ShouldBe(built);
    }

    [Fact]
    public void Find_ArtifactsLayoutBuiltReleaseOnly_ResolvesTheReleaseOutput()
    {
        // The other field layout: <UseArtifactsOutput>true</UseArtifactsOutput> built `-c Release` only, so the
        // evaluated `debug` directory was never written. The decoy is another project's output carrying the
        // same assembly file name — out of scope by anchor construction rather than by ranking, because the
        // search answers at the deepest existing directory (artifacts/bin/MyApp.Arch) and widening never
        // reaches past an anchor that yielded a candidate.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssembly(temp, "artifacts", "bin", "MyApp.Arch", "release", Assembly);
        string intermediate = WriteAssembly(temp, "artifacts", "obj", "MyApp.Arch", "release", Assembly);
        WriteAssembly(temp, "artifacts", "bin", "MyApp.Web", "release", Assembly);
        string evaluated = temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug", Assembly);

        BuiltOutputProbe.Find(evaluated, intermediate)
            .ShouldBe(built);
    }

    [Fact]
    public void Find_IntermediateAssemblyIsReachableFromTheAnchor_RefusesIt()
    {
        // BaseIntermediateOutputPath redirected under the output root — the one tree where the anchor rule
        // alone does not keep the intermediate assembly out of scope, and the whole reason the refusal exists
        // rather than being left implied by the walk.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string intermediate = WriteAssembly(temp, "bin", "obj", "Debug", "net10.0", Assembly);
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, intermediate)
            .ShouldBeNull();
    }

    [Fact]
    public void Find_IntermediateAssemblyReachable_WithoutTheSignal_WouldHaveReturnedIt()
    {
        // The negative control for the fact above: the same tree, the intermediate path withheld. Without it
        // the search answers with the obj-side assembly, which is what proves the refusal — and not the anchor
        // walk — is what rejects it. An artifacts-shaped tree cannot serve as this control: there the ceiling
        // stops the walk at artifacts/bin before obj is ever in scope.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string intermediate = WriteAssembly(temp, "bin", "obj", "Debug", "net10.0", Assembly);
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(intermediate);
    }

    [Fact]
    public void Find_EvaluatedDirectoryCreatedEmptyByTheDesignTimeBuild_WidensToTheProjectDirectoryAndResolves()
    {
        // What the live run measured and the tree above cannot: check's own workspace load runs a design-time
        // build, and the SDK creates the evaluated output directory — empty — before resolution reads the
        // disk. Anchoring on bare existence trapped the search in that empty directory and refused a fully
        // built solution; the widening is what makes the artifacts layout resolve in a live run rather than
        // only in this suite. The decoy pins that widening stops at the first anchor that answers, so
        // MyApp.Web's copy is still never in scope.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssembly(temp, "artifacts", "bin", "MyApp.Arch", "release", Assembly);
        WriteAssembly(temp, "artifacts", "bin", "MyApp.Web", "release", Assembly);
        Directory.CreateDirectory(temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug"));
        string evaluated = temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(built);
    }

    [Fact]
    public void Find_OutputRootExistsButHoldsNoBuild_ReturnsNull()
    {
        // The nothing-built refusal survives the widening: a chain of existing-but-empty directories has
        // nowhere to widen past the ceiling, so the loud "build the solution first" answer is unchanged.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        Directory.CreateDirectory(temp.PathOf("bin", "Debug", "net10.0"));
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBeNull();
    }

    [Fact]
    public void Find_SiblingConfigurationInTheStandardLayout_ResolvesIt()
    {
        // The release-CI regression at search level: `dotnet build -c Release`, then a Debug-evaluated path
        // that names a DLL nobody built.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssembly(temp, "bin", "Release", "net10.0", Assembly);
        string intermediate = WriteAssembly(temp, "obj", "Release", "net10.0", Assembly);
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, intermediate)
            .ShouldBe(built);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("refint")]
    [InlineData("publish")]
    [InlineData("runtimes")]
    public void Find_OnlyAnExcludedSegmentMatches_ReturnsNull(string excludedSegment)
    {
        // A reference assembly has no IL bodies, so loading one as the spec fails inside Define() rather than
        // at load — the failure mode furthest from its cause. Nothing built beats the wrong thing built.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        WriteAssembly(temp, "bin", "Debug", "net10.0", excludedSegment, Assembly);
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBeNull();
    }

    [Fact]
    public void Find_RealOutputBesideAnExcludedCopy_PrefersTheRealOutput()
    {
        // The excluded copy is written later and sits at the same depth below the anchor, so neither ranking
        // key can be what rejects it — only the deny list.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssemblyAt(Older, temp, "bin", "Debug", "net10.0", Assembly);
        WriteAssemblyAt(Newer, temp, "bin", "Debug", "ref", Assembly);
        string evaluated = temp.PathOf("bin", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(built);
    }

    [Fact]
    public void Find_NestedRuntimeIdentifierCopy_PrefersTheShallowerOutput()
    {
        // Ranking key one: the canonical output beats a RID copy nested below it, even when the copy is newer.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string built = WriteAssemblyAt(Older, temp, "bin", "Debug", "net10.0", Assembly);
        WriteAssemblyAt(Newer, temp, "bin", "Debug", "net10.0", "win-x64", Assembly);
        string evaluated = temp.PathOf("bin", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(built);
    }

    [Fact]
    public void Find_TwoBuiltConfigurations_PrefersTheMostRecentlyWritten()
    {
        // Ranking key two, at equal depth. Debug sorts first ordinally, so the last-written rule — the one
        // the release-CI behaviour rests on — is provably what picks Release here.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        WriteAssemblyAt(Older, temp, "bin", "Debug", "net10.0", Assembly);
        string newest = WriteAssemblyAt(Newer, temp, "bin", "Release", "net10.0", Assembly);
        string evaluated = temp.PathOf("bin", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(newest);
    }

    [Fact]
    public void Find_SpecProjectNeverBuiltButACopyExistsUnderTheOutputRoot_ResolvesTheCopy()
    {
        // The residual trade, written down as a fact rather than as prose: under a shared output root, a
        // consumer project's own directory holds a copy of the spec assembly, and the spec project's build is
        // absent. The search answers with the copy. Loading a copy of the same assembly is a far smaller
        // failure than refusing a built solution, which is what the alternative — no search at all — does.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string copy = WriteAssembly(temp, "bin", "MyApp.Tests", "net10.0", Assembly);
        string evaluated = temp.PathOf("bin", "MyApp.Arch", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBe(copy);
    }

    [Fact]
    public void Find_NoOutputRootAncestor_ReturnsNull()
    {
        // The accepted narrowing, pinned: a repository whose output root is named something else keeps its
        // primary resolution and loses the cross-configuration fallback it incidentally had. That is the price
        // of a search that can never climb to a repository or a drive root, and it is not parity with the
        // arithmetic this replaced.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        WriteAssembly(temp, "out", "Release", "net10.0", Assembly);
        string evaluated = temp.PathOf("out", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBeNull();
    }

    [Fact]
    public void Find_NothingBuilt_ReturnsNull()
    {
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        string evaluated = temp.PathOf("bin", "Debug", "net10.0", Assembly);

        BuiltOutputProbe.Find(evaluated, null)
            .ShouldBeNull();
    }

    [Fact]
    public void Find_UnrootedOrEmptyEvaluatedPath_ReturnsNullWithoutTouchingTheWorkingDirectory()
    {
        // An unrooted path would resolve against the current directory, which is never what an MSBuild
        // evaluation meant: the answer would depend on where the CLI happened to be invoked from.
        BuiltOutputProbe.Find("", null)
            .ShouldBeNull();
        BuiltOutputProbe.Find("   ", null)
            .ShouldBeNull();
        BuiltOutputProbe.Find(Path.Combine("bin", "Debug", "net10.0", Assembly), null)
            .ShouldBeNull();
    }

    [Fact]
    public void AnchorFor_DescendsToTheDeepestExistingDirectoryBelowTheOutputRoot()
    {
        // The named directory is the ceiling of the walk, never the anchor. Starting at artifacts/bin would
        // pull every other project's output into scope and make the right answer a ranking problem; the
        // search begins here and widens only while it finds nothing.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        WriteAssembly(temp, "artifacts", "bin", "MyApp.Arch", "release", Assembly);
        string evaluated = temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug", Assembly);

        BuiltOutputProbe.AnchorFor(evaluated)
            .ShouldBe(temp.PathOf("artifacts", "bin", "MyApp.Arch"));
    }

    [Fact]
    public void AnchorChainFor_ListsEveryExistingDirectoryUpToTheOutputRoot_DeepestFirst()
    {
        // The widening order, pinned end to end: deepest existing directory first, the named output root
        // last, nothing above it ever a candidate.
        using TempDirectory temp = TestTempRoot.Fresh("built-output-probe");
        Directory.CreateDirectory(temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug"));
        string evaluated = temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug", Assembly);

        BuiltOutputProbe.AnchorChainFor(evaluated)
            .ShouldBe([
                temp.PathOf("artifacts", "bin", "MyApp.Arch", "debug"),
                temp.PathOf("artifacts", "bin", "MyApp.Arch"),
                temp.PathOf("artifacts", "bin")
            ]);
    }

    // A zero-byte assembly at the given path, with its directory created.
    private static string WriteAssembly(TempDirectory temp, params string[] segments)
    {
        string path = temp.PathOf(segments);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    // The same, with an explicit write time — for the facts where a ranking key is the thing under test. The
    // time leads so the path can stay a params list.
    private static string WriteAssemblyAt(DateTime writtenAtUtc, TempDirectory temp, params string[] segments)
    {
        string path = WriteAssembly(temp, segments);
        File.SetLastWriteTimeUtc(path, writtenAtUtc);
        return path;
    }
}

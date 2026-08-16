using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     Spec resolution by convention against a real <c>MSBuildWorkspace</c> load, over the two output
///     layouts the skew matrix measured refusing a fully built solution: a parent
///     <c>Directory.Build.props</c> carrying a flat <c>OutputPath</c>, and <c>UseArtifactsOutput</c>. One
///     fixture, one props overlay per arm, so the layout is provably the only variable.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a live load rather than another temp tree.</b> <c>Cli/BuiltOutputProbeTests</c> pins the
///         search over hand-built trees and <c>Roslyn/IntermediateOutputTreeTests</c> pins the derivation as
///         arithmetic, but both fabricate disk state <em>after</em> any load. One axis therefore sits
///         structurally outside them: <c>check</c>'s own workspace load runs a design-time build, and the SDK
///         creates the <em>evaluated</em> output directory — empty — before spec resolution reads the disk.
///         That is measured here, on the artifacts arm, and it is what the widening exists for.
///     </para>
///     <para>
///         <b>Why <c>-c Release</c>.</b> It is what makes both arms non-vacuous. <c>Configuration</c> arrives
///         as a global property for the real build and not for the design-time build, so the path MSBuild
///         evaluates names a directory no build ever wrote — which is the field shape exactly. Each arm
///         asserts that claim on disk before running anything: without it a fact could pass on
///         <c>RequireBuiltOutput</c>'s primary <c>File.Exists</c> branch and prove nothing.
///     </para>
///     <para>
///         <b>What the two arms do not share.</b> Only the artifacts layout leaves an empty evaluated
///         directory behind, because there the evaluated path names a <c>debug</c> directory nothing had
///         created. On the hostile-props layout the evaluated path's own directory is <c>bin</c>, which the
///         real build already made, and the design-time build adds only intermediate <c>obj/Debug/</c>
///         directories — so that arm pins the resolution and records the measurement rather than inventing an
///         assertion for it.
///     </para>
///     <para>
///         <b>Cost, measured rather than guessed.</b> Each arm is a private copy, a real
///         <c>dotnet restore</c>, a real <c>dotnet build -c Release</c> and a cold workspace load. Over four
///         green runs of the filtered suite that came to <b>8–23 s per fact</b> — both facts landing within a
///         few seconds of each other every time — and 29–54 s for the whole <c>dotnet test</c> invocation
///         including its build and discovery. That is the same band as
///         <c>ArtifactsOutputCacheE2ETests</c>, measured beside it at 11–12 s per fact and 40 s end to end,
///         so the real build each arm adds over that class's restore-only arms costs less than the
///         run-to-run spread. A third arm costs the same again: add one only for a layout that is not a
///         reshuffle of these two.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class OutputLayoutSpecResolutionE2ETests
{
    private const string SpecAssembly = LayoutAppFixture.SpecAssembly;

    private const string SpecProject = LayoutAppFixture.SpecProject;

    private const string RuleId = LayoutAppFixture.RuleId;

    // The field layout: a parent props file spelling the configuration into OutputPath. A props file is
    // imported before the SDK defaults Configuration, so the evaluation yields a flat `bin\` while every
    // real build lands one level down.
    private static readonly string[] HostilePropsLayout =
    [
        @"<OutputPath>bin\$(Configuration)\</OutputPath>",
        "<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>"
    ];

    // The other field layout. UseArtifactsOutput is only honoured from a Directory.Build.props, which is one
    // more reason the arm writes one rather than setting the property per project.
    private static readonly string[] ArtifactsLayout = ["<UseArtifactsOutput>true</UseArtifactsOutput>"];

    [Fact]
    public async Task Check_FlatOutputPathFromAParentPropsFile_ResolvesTheSpecTheBuildActuallyWrote()
    {
        using TempFixtureWorkspace workspace = BuiltCopy(HostilePropsLayout);

        // The layout claim, asserted rather than assumed.
        string built = workspace.PathOf(SpecProject, "bin", "Release", SpecAssembly);
        string evaluated = workspace.PathOf(SpecProject, "bin", SpecAssembly);
        File.Exists(built)
            .ShouldBeTrue($"the -c Release build should have written '{built}'.");
        File.Exists(evaluated)
            .ShouldBeFalse($"the workspace evaluates '{evaluated}', which no build ever writes.");

        // Cold so a real design-time build runs; --no-cache so no hit replays a recorded resolution instead
        // of resolving.
        CliResult result = await CliRunner.InvokeColdAsync("check", workspace.SolutionPath, "--no-cache");

        result.ShouldSucceed($"pass {RuleId}");

        // What the run measured, since there is nothing to pin: the design-time build created no output
        // directory here at all — only obj/Debug/ and its ref/refint children — because the evaluated path's
        // own directory is `bin`, which the real build already made. The empty-evaluated-directory hazard is
        // the artifacts arm's; this arm's is that `bin\` is a real directory holding no build. So the fact
        // worth holding after the run is that the evaluated file is still absent: the answer came from the
        // search over the tree the build wrote, not from anything the load materialised.
        File.Exists(evaluated)
            .ShouldBeFalse($"nothing in the run should have written '{evaluated}'.");
    }

    [Fact]
    public async Task Check_ArtifactsLayoutBuiltReleaseOnly_ResolvesThroughTheEmptyEvaluatedDirectory()
    {
        using TempFixtureWorkspace workspace = BuiltCopy(ArtifactsLayout);

        string built = workspace.PathOf("artifacts", "bin", SpecProject, "release", SpecAssembly);
        string evaluatedDirectory = workspace.PathOf("artifacts", "bin", SpecProject, "debug");
        File.Exists(built)
            .ShouldBeTrue($"the -c Release build should have written '{built}'.");
        Directory.Exists(evaluatedDirectory)
            .ShouldBeFalse($"nothing has built a debug configuration, so '{evaluatedDirectory}' must not exist yet.");

        CliResult result = await CliRunner.InvokeColdAsync("check", workspace.SolutionPath, "--no-cache");

        result.ShouldSucceed($"pass {RuleId}");

        // The axis no hand-built tree can reach, measured live rather than described: the design-time build
        // the load ran created the evaluated output directory — and left it empty — before spec resolution
        // read the disk. Anchoring on bare existence traps the search in that shell and refuses a fully built
        // solution, which is the field defect; the widening is what carries the answer up to `release`.
        Directory.Exists(evaluatedDirectory)
            .ShouldBeTrue($"the design-time build was expected to create '{evaluatedDirectory}'.");
        List<string> leftBehind = Directory.EnumerateFileSystemEntries(evaluatedDirectory)
            .ToList();
        leftBehind.ShouldBeEmpty(
            "the evaluated directory the design-time build created should hold no build output — that "
            + "emptiness is what the widening exists to see past.");
    }

    // A private copy of the fixture, built for real under the requested layout.
    private static TempFixtureWorkspace BuiltCopy(IEnumerable<string> layoutProperties)
    {
        // Dedicated, not leased: the leased copy resets by pruning anything the fixture source does not
        // contain, which would delete the build output the arm exists to produce.
        TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(
            LayoutAppFixture.FixtureDirectory, LayoutAppFixture.SolutionFileName, restore: false);
        string workingDirectory = Path.GetDirectoryName(workspace.SolutionPath)!;

        File.WriteAllText(workspace.PathOf("Directory.Build.props"), LayoutAppFixture.PropsFile(layoutProperties));
        FixtureRestorer.Restore(workspace.SolutionPath);

        // --disable-build-servers (plus DotnetCli's node/server env) keeps the drained child from leaving a
        // persistent worker that would wedge the output pipe.
        DotnetCli.Run($"build \"{workspace.SolutionPath}\" -c Release --disable-build-servers", workingDirectory);

        return workspace;
    }
}

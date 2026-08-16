using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The persisted extraction cache against a real <c>UseArtifactsOutput</c> tree, with the default layout
///     beside it as the control — the output layout is the only variable. Both arms restore a private copy of
///     the fixture with the real SDK, so what is under test is not only the path derivation but the claim it
///     rests on: that the assets file lands where <see cref="IntermediateOutputTree" /> looks for it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a live restore rather than a fabricated tree.</b>
///         <c>Caching/ExtractionCacheStoreTests</c> already pins the store's behaviour over a hand-built
///         artifacts-shaped tree, and <c>Roslyn/IntermediateOutputTreeTests</c> pins the derivation as
///         arithmetic. Neither can know where the SDK actually writes <c>project.assets.json</c>, and that is
///         exactly what the skew matrix found wrong in the field: the probe watched
///         <c>&lt;projectDirectory&gt;/obj/project.assets.json</c>, a path this layout never creates, so a
///         re-restore that changed the model was served from cache as though nothing had moved.
///     </para>
///     <para>
///         <b>Why the mutation is a byte append.</b> The third run has to load a workspace, so the assets
///         file must stay readable by the SDK; trailing whitespace changes the bytes the store stamps without
///         changing what NuGet parses. The semantic version of the same probe — a package removed through an
///         imported props file the cache does not stamp, then a real re-restore — is the field leg, and it is
///         recorded there rather than paid for on every suite run.
///     </para>
///     <para>
///         Both arms take a dedicated copy: the leased copy resets by pruning anything the fixture source
///         does not contain, which would delete the <c>artifacts/</c> tree the arm exists to produce.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class ArtifactsOutputCacheE2ETests
{
    private const string Web = "MyApp.Web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArtifactsLayout_AssetsFileChangedBetweenRuns_IsSeen()
    {
        using TempFixtureWorkspace workspace = RestoredCopy(true);
        using var cache = new TempCacheRoot("artifacts-output-cache");

        // The layout claim, asserted rather than assumed: the assets file is outside the project directory.
        string assets = workspace.PathOf("artifacts", "obj", Web, "project.assets.json");
        File.Exists(assets)
            .ShouldBeTrue();
        File.Exists(workspace.PathOf(Web, "obj", "project.assets.json"))
            .ShouldBeFalse();

        await RunGraphAsync(workspace, cache.Root);
        (await RunGraphAsync(workspace, cache.Root)).ShouldBe(CodebaseSourceOutcome.Hit);

        await File.AppendAllTextAsync(assets, "\n", Ct);

        (await RunGraphAsync(workspace, cache.Root)).ShouldNotBe(CodebaseSourceOutcome.Hit);
    }

    [Fact]
    public async Task DefaultLayout_AssetsFileChangedBetweenRuns_IsSeen()
    {
        using TempFixtureWorkspace workspace = RestoredCopy(false);
        using var cache = new TempCacheRoot("artifacts-output-cache");

        string assets = workspace.PathOf(Web, "obj", "project.assets.json");
        File.Exists(assets)
            .ShouldBeTrue();

        await RunGraphAsync(workspace, cache.Root);
        (await RunGraphAsync(workspace, cache.Root)).ShouldBe(CodebaseSourceOutcome.Hit);

        await File.AppendAllTextAsync(assets, "\n", Ct);

        (await RunGraphAsync(workspace, cache.Root)).ShouldNotBe(CodebaseSourceOutcome.Hit);
    }

    // A private fixture copy, restored under the requested layout. UseArtifactsOutput is only honoured from
    // a Directory.Build.props, which is why the arm writes one rather than setting the property per project.
    private static TempFixtureWorkspace RestoredCopy(bool artifactsLayout)
    {
        TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(restore: false);
        if (artifactsLayout)
            File.WriteAllText(
                workspace.PathOf("Directory.Build.props"),
                "<Project>\n    <PropertyGroup>\n        <UseArtifactsOutput>true</UseArtifactsOutput>\n"
                + "    </PropertyGroup>\n</Project>\n");

        FixtureRestorer.Restore(workspace.SolutionPath);
        return workspace;
    }

    private static async Task<CodebaseSourceOutcome?> RunGraphAsync(TempFixtureWorkspace workspace, string cacheRoot)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        FakeEnvironment environment = new FakeEnvironment().SetVariable(LoadBearingEnvVars.CacheDirectory, cacheRoot);
        var runner = new GraphRunner(output, error, new ColdSolutionSource(), environment);
        string workingDirectory = SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath);

        int exit = await runner.RunAsync(
            new GraphRequest(
                workspace.SolutionPath, true, workingDirectory, false, null, false, DocumentGrain.Full, null),
            Ct);

        exit.ShouldBe(0, error.ToString());
        return runner.LastOutcome;
    }
}

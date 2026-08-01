using Shouldly;
using Xunit;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Dogfood;

/// <summary>
///     The dogfood gates. One class so the workspace-heavy runs serialize.
///     <see cref="SelfSpec_Check_ExitsZero" /> is the CI-equivalent self-spec gate: LoadBearing checks
///     itself and passes. <see cref="AgentsMd_IsCurrent" /> is the provably-current gate, no workspace
///     needed: it composes the root block in-process and asserts the committed <c>AGENTS.md</c>'s single
///     managed block equals it exactly — the product thesis in one test.
///     <see cref="ArchitectureMd_IsCurrent" /> is the same gate over the rendered diagram, and unlike its
///     pure-spec sibling it does need a workspace: the diagram is drawn from the codebase rather than from
///     the spec, so the solution has to be loaded and extracted to compose it.
/// </summary>
[Collection("Serial")]
public sealed class SelfSpecTests
{
    /// <summary>
    ///     The projects the committed diagram draws: the four shipping packages, the rule pack, and the
    ///     spec project. An allow-list rather than a deny-list, because the fixture projects outnumber
    ///     these two to one and an allow-list keeps the committed artifact stable while they churn. The
    ///     cost is that a genuinely new shipping project stays off the diagram until someone adds it here,
    ///     which no gate can catch.
    /// </summary>
    private static readonly string[] ShippingProjects =
    [
        "Zphil.LoadBearing",
        "Zphil.LoadBearing.Cli",
        "Zphil.LoadBearing.Roslyn",
        "Zphil.LoadBearing.Xunit",
        "Zphil.LoadBearing.Packs.DotNet",
        "Zphil.LoadBearing.ArchSpec"
    ];

    [Fact]
    public async Task SelfSpec_Check_ExitsZero()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost).
        CliResult result = await CliRunner.InvokeAsync("check", RepoRoot.Solution, "--spec", RepoRoot.ArchSpecCsproj);

        // Surface the CLI's own output on failure — otherwise a red self-check (e.g. the Release-only
        // spec-resolution regression) shows only "2 != 0" with no clue why, as the release run did.
        result.Exit.ShouldBe(0, $"check exited {result.Exit}.\nstderr:\n{result.Err}\nstdout:\n{result.Out}");
    }

    [Fact]
    public void AgentsMd_IsCurrent()
    {
        ArchitectureModel model = ArchModelBuilder.Build(new LoadBearingArchSpec());
        string composed = AgentContextRenderer.RootBlock(model, "Zphil.LoadBearing.ArchSpec");
        string committed = File.ReadAllText(RepoRoot.AgentsMd);

        // Exactly one marker pair (ExtractBody throws on any other count), and its body is current.
        MarkerPairCount(committed).ShouldBe(1);
        ManagedBlock.ExtractBody(committed).ShouldBe(composed);
    }

    [Fact]
    public async Task ArchitectureMd_IsCurrent()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost). The
        // extraction excludes nothing, which is the call `graph` makes: the diagram and the survey are two
        // renderings of one codebase and must never disagree about what is in it.
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(RepoRoot.Solution);
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(loaded.Solution);

        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string composed = GraphDiagramRenderer.Block(
            summary, Path.GetFileName(RepoRoot.Solution), new DiagramScope(ShippingProjects, []));
        string committed = File.ReadAllText(RepoRoot.ArchitectureMd);

        MarkerPairCount(committed).ShouldBe(1);
        ManagedBlock.ExtractBody(committed).ShouldBe(composed);
    }

    private static int MarkerPairCount(string text)
    {
        int begins = Occurrences(text, ManagedBlock.BeginMarker);
        int ends = Occurrences(text, ManagedBlock.EndMarker);
        return begins == ends ? begins : -1;
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
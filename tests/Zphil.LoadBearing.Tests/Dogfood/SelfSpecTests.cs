using Shouldly;
using Xunit;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Dogfood;

/// <summary>
///     The dogfood gates (Phase 4 acceptance). One class so the two workspace-heavy runs serialize.
///     <see cref="SelfSpec_Check_ExitsZero" /> is the CI-equivalent self-spec gate: LoadBearing checks
///     itself and passes. <see cref="AgentsMd_IsCurrent" /> is the provably-current gate, no workspace
///     needed: it composes the root block in-process and asserts the committed <c>AGENTS.md</c>'s single
///     managed block equals it exactly — the product thesis in one test.
/// </summary>
[Collection("Serial")]
public sealed class SelfSpecTests
{
    [Fact]
    public async Task SelfSpec_Check_ExitsZero()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — accepted for Phase 4).
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
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn.Diagnostics;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The composition seam: <see cref="WorkspaceDiagnostics.Rendered" /> is where the MSBuild-selection
///     note joins the diagnostics, so that the one list can be handed to <em>both</em> the stderr echo and
///     the JSON document. Appending it at write time reached stderr alone, which is the whole reason the MCP
///     surface lost it: the tools pass <see cref="TextWriter.Null" /> there. Pure over an in-memory value,
///     so no workspace is opened; the note's own text and the rendered-once-per-verb behaviour are pinned by
///     <c>WorkspaceDiagnosticsGateE2ETests</c>.
///     <para>
///         The last two rows share the file's economics rather than its subject: they pin what
///         <see cref="WorkspaceTrustStamp.From(WorkspaceDiagnostics, string)" /> makes of an unsupported
///         project, over a hand-built <see cref="WorkspaceDiagnostics" /> and no bed at all — which is what a
///         fixture solution per project kind would have cost.
///     </para>
/// </summary>
public sealed class WorkspaceDiagnosticsRendererTests
{
    [Fact]
    public void Rendered_EmptyDiagnostics_StaysEmpty()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run must say nothing about MSBuild on any surface, including the document.
        IReadOnlyList<string> composed = new WorkspaceDiagnostics([], [], [], [], [], [], []).Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void Rendered_NonEmptyDiagnostics_KeepsThemInOrderAndAppendsTheNoteLast()
    {
        IReadOnlyList<string> composed = new WorkspaceDiagnostics(["first failure", "second failure"], [], [], [], [], [], []).Rendered;

        composed.Count.ShouldBe(3);
        composed[0]
            .ShouldBe("first failure");
        composed[1]
            .ShouldBe("second failure");
        composed[2]
            .ShouldBe(MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void Rendered_MergeNotesPresent_LeavesThemOutOfTheFiveVerbRendering()
    {
        // The asymmetry, stated as a test: only check renders merge notes, and it asks for them by name.
        // Rendered is what the other five write, so a merge note must not reach it — and a run with nothing
        // but merge notes says nothing at all on those surfaces.
        IReadOnlyList<string> composed = new WorkspaceDiagnostics([], ["a conflation advisory"], [], [], [], [], []).Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void RenderedWithMergeNotes_LoadFailuresFirstThenNotesThenTheNote()
    {
        IReadOnlyList<string> composed = new WorkspaceDiagnostics(["a load failure"], ["a conflation advisory"], [], [], [], [], [])
            .RenderedWithMergeNotes;

        composed.Count.ShouldBe(3);
        composed[0]
            .ShouldBe("a load failure");
        composed[1]
            .ShouldBe("a conflation advisory");
        composed[2]
            .ShouldBe(MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void Render_WritesWhatItIsGivenAndNothingMore()
    {
        // Render no longer appends: it echoes the composed list. Handing it a raw list therefore produces no
        // note — which is what keeps the two surfaces reading the same one list rather than each minting one.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, ["a load failure"]);

        error.ToString()
            .ShouldBe($"warning: a load failure{Environment.NewLine}");
    }

    [Fact]
    public void Render_Json_DropsTheWarningPrefix()
    {
        // Under --json the diagnostics are structured data in the document and stderr is their unadorned
        // echo, so the prefix would be noise on a stream a hook may be reading alongside stdout.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, ["a load failure"], true);

        error.ToString()
            .ShouldBe($"a load failure{Environment.NewLine}");
    }

    [Fact]
    public void From_ASharedProjectBesideAnFsproj_GivesThemDifferentReasons()
    {
        // The mislabel, as a pin. One reason string was stamped onto every path, so a .shproj — a container
        // whose .projitems compile into the projects that import it, very often C# and already in the model
        // through them — was reported as "not a C# project" on every surface. Both kinds in one stamp,
        // because what went wrong was not the wording of either but that one sentence covered both.
        var diagnostics = new WorkspaceDiagnostics(
            [], [], [], [], [],
            [
                new UnsupportedProject("/repo/Signals/Signals.fsproj", UnsupportedProjectKind.NotCsharp),
                new UnsupportedProject("/repo/Shared/Shared.shproj", UnsupportedProjectKind.SharedProject)
            ],
            []);

        WorkspaceTrustStamp stamp = WorkspaceTrustStamp.From(diagnostics, "/repo");

        (stamp.UnsupportedProjects ?? []).Select(project => project.Describe())
            .ShouldBe([
                "Signals/Signals.fsproj — not a C# project",
                "Shared/Shared.shproj — a shared project, compiled into the projects that import it"
            ]);
    }

    [Fact]
    public void From_NoUnsupportedProjects_OmitsTheKeyRatherThanWritingAnEmptyList()
    {
        // The empty-to-null policy, on the slot whose type changed: a run that reached every declared project
        // says nothing, which is what keeps a clean document byte-identical to the one it always was.
        WorkspaceTrustStamp stamp = WorkspaceTrustStamp.From(WorkspaceDiagnostics.None, "/repo");

        stamp.UnsupportedProjects.ShouldBeNull();
    }
}

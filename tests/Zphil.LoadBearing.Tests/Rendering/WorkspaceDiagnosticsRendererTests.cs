using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The composition seam: <see cref="WorkspaceDiagnostics.Rendered" /> is where the MSBuild-selection
///     note joins the diagnostics, so that the one list can be handed to <em>both</em> the stderr echo and
///     the JSON document. Appending it at write time reached stderr alone, which is the whole reason the MCP
///     surface lost it: the tools pass <see cref="TextWriter.Null" /> there. Pure over an in-memory value,
///     so no workspace is opened; the note's own text and the rendered-once-per-verb behaviour are pinned by
///     <c>WorkspaceDiagnosticsGateE2ETests</c>.
/// </summary>
public sealed class WorkspaceDiagnosticsRendererTests
{
    [Fact]
    public void Rendered_EmptyDiagnostics_StaysEmpty()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run must say nothing about MSBuild on any surface, including the document.
        var composed = new WorkspaceDiagnostics([], [], []).Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void Rendered_NonEmptyDiagnostics_KeepsThemInOrderAndAppendsTheNoteLast()
    {
        var composed = new WorkspaceDiagnostics(["first failure", "second failure"], [], []).Rendered;

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
        var composed = new WorkspaceDiagnostics([], ["a conflation advisory"], []).Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void RenderedWithMergeNotes_LoadFailuresFirstThenNotesThenTheNote()
    {
        var composed = new WorkspaceDiagnostics(["a load failure"], ["a conflation advisory"], [])
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
}

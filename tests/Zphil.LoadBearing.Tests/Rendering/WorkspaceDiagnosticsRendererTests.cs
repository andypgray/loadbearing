using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn.MsBuild;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The composition seam: <see cref="WorkspaceDiagnosticsRenderer.Compose" /> is where the
///     MSBuild-selection note joins the diagnostics, so that the one list can be handed to <em>both</em>
///     the stderr echo and the JSON document. Appending it at write time reached stderr alone, which is
///     the whole reason the MCP surface lost it: the tools pass <see cref="TextWriter.Null" /> there.
///     Pure over an in-memory list, so no workspace is opened; the note's own text and the
///     rendered-once-per-verb behaviour are pinned by <c>WorkspaceDiagnosticsGateE2ETests</c>.
/// </summary>
public sealed class WorkspaceDiagnosticsRendererTests
{
    [Fact]
    public void Compose_EmptyDiagnostics_StaysEmpty()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run must say nothing about MSBuild on any surface, including the document.
        var composed = WorkspaceDiagnosticsRenderer.Compose([]);

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void Compose_NonEmptyDiagnostics_KeepsThemInOrderAndAppendsTheNoteLast()
    {
        var composed = WorkspaceDiagnosticsRenderer.Compose(["first failure", "second failure"]);

        composed.Count.ShouldBe(3);
        composed[0].ShouldBe("first failure");
        composed[1].ShouldBe("second failure");
        composed[2].ShouldBe(MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void Render_WritesWhatItIsGivenAndNothingMore()
    {
        // Render no longer appends: it echoes the composed list. Handing it a raw list therefore produces no
        // note — which is what keeps the two surfaces reading the same one list rather than each minting one.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, ["a load failure"]);

        error.ToString().ShouldBe($"warning: a load failure{Environment.NewLine}");
    }

    [Fact]
    public void Render_Json_DropsTheWarningPrefix()
    {
        // Under --json the diagnostics are structured data in the document and stderr is their unadorned
        // echo, so the prefix would be noise on a stream a hook may be reading alongside stdout.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, ["a load failure"], true);

        error.ToString().ShouldBe($"a load failure{Environment.NewLine}");
    }
}

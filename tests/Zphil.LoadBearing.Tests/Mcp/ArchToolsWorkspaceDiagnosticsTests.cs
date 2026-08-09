using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Mcp.Tools;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The MSBuild-selection note on the MCP surface, which is the one line this surface used to destroy.
/// </summary>
/// <remarks>
///     <para>
///         <c>ArchTools</c> hands every runner <see cref="TextWriter.Null" /> as the error writer, and its
///         claim that "everything the error writer would carry is in the document" was true of all of it
///         but this: the note was appended at <em>write</em> time, to stderr, and nothing else.
///         Composing it into the list both surfaces read makes the claim true, and this is where that is
///         held — a client that sees a load failure also sees which MSBuild produced it, which is nearly
///         always the next question.
///     </para>
///     <para>
///         The tool is constructed directly rather than driven through <see cref="McpPipelineHarness" />,
///         because the subject is what the tool passes to its runner: the harness composes its own
///         <c>ISolutionSource</c> from DI, and the injected diagnostics are the only way to reach the note
///         over a fixture that loads perfectly well. Serial: the injecting source wraps a real load.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class ArchToolsWorkspaceDiagnosticsTests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchCheck_WorkspaceLoadDiagnostic_CarriesTheMsBuildNoteLastInTheDocument()
    {
        var tools = new ArchTools(Binding(), new DiagnosticInjectingSolutionSource([LoadDiagnostic]));

        string document = await tools.CheckAsync(cancellationToken: Ct);

        WorkspaceDiagnosticsOf(document)
            .ShouldBe([LoadDiagnostic, WorkspaceDiagnosticsRenderer.MsBuildNote()]);
    }

    [Fact]
    public async Task ArchCheck_NoWorkspaceDiagnostics_CarriesNoNote()
    {
        // The negative control that keeps the note diagnostic context rather than a banner: an empty
        // composition stays empty, so a clean call's document says nothing about MSBuild at all.
        var tools = new ArchTools(Binding(), new DiagnosticInjectingSolutionSource([]));

        string document = await tools.CheckAsync(cancellationToken: Ct);

        WorkspaceDiagnosticsOf(document).ShouldBeEmpty();
    }

    private static McpServerBinding Binding()
    {
        string solution = CliRunner.MyAppSolution;
        return new McpServerBinding(solution, CliRunner.CleanSpecDll, Path.GetDirectoryName(Path.GetFullPath(solution))!);
    }

    private static string[] WorkspaceDiagnosticsOf(string document)
    {
        using JsonDocument parsed = JsonDocument.Parse(document);
        return parsed.RootElement.GetProperty("workspaceDiagnostics")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }
}

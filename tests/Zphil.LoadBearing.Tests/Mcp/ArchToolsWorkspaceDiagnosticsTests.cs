using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Tools;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.MsBuild;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The MSBuild-selection note on the MCP surface, which is the one line this surface used to destroy —
///     and the incomplete-model verdict beside it, which is the whole refusal a surface with no exit code has.
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

    private const string UnrestoredProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    [Fact]
    public async Task ArchCheck_WorkspaceLoadDiagnostic_CarriesTheMsBuildNoteLastInTheDocument()
    {
        var tools = new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource([LoadDiagnostic]),
            ResponseFitter.FirstRung);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBe([LoadDiagnostic, MsBuildBootstrap.SelectionNote()]);
    }

    [Fact]
    public async Task ArchCheck_NoWorkspaceDiagnostics_CarriesNoNote()
    {
        // The negative control that keeps the note diagnostic context rather than a banner: an empty
        // composition stays empty, so a clean call's document says nothing about MSBuild at all.
        var tools = new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource([]),
            ResponseFitter.FirstRung);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        CheckJson.Strings(document, "workspaceDiagnostics")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchCheck_RestoreFailedProject_StampsTheModelIncompleteVerdict()
    {
        // The MCP surface has no exit code, so <c>modelIncomplete</c> is the whole refusal a client can read.
        // It follows the gate rather than either cause, which is exactly why a second cause could be added
        // without touching a single tool.
        var tools = new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource([], restoreFailedProjects: [UnrestoredProject]),
            ResponseFitter.FirstRung);

        string document = await tools.CheckAsync(cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(document);
        parsed.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ArchGraph_RestoreFailedProject_RefusesWithTheRestoreWordingAndBothDialectsOfTheOptOut()
    {
        // graph carries its evidence inline because there is nothing above it here — this tool discards its
        // error writer — and it is the entry point a stranger reaches for first, so the refusal names the
        // remedy and the opt-out in both dialects rather than assuming which surface asked.
        var tools = new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource([], restoreFailedProjects: [UnrestoredProject]),
            ResponseFitter.FirstRung);

        var error = await Should.ThrowAsync<UserErrorException>(() => tools.GraphAsync(cancellationToken: Ct));

        error.Message.ShouldContain(
            "the model is incomplete — NuGet packages did not resolve for 1 project, so graph cannot survey "
            + "the codebase: the external references a survey exists to show are exactly what did not "
            + "resolve:");
        error.Message.ShouldContain(UnrestoredProject);
        error.Message.ShouldContain("Restore the solution first (dotnet restore)");
        error.Message.ShouldContain("allowWorkspaceDiagnostics: true (arch_graph)");
    }

    [Fact]
    public async Task ArchGraph_RestoreFailedProjectWithTheOptOut_SurveysThePartialModel()
    {
        // The opt-out the refusal names, taken: one flag for one question, whichever way the model came up
        // short.
        var tools = new ArchTools(
            McpServerBindings.MyAppWithCleanSpec(),
            new DiagnosticInjectingSolutionSource([], restoreFailedProjects: [UnrestoredProject]),
            ResponseFitter.FirstRung);

        string document = await tools.GraphAsync(true, cancellationToken: Ct);

        using JsonDocument parsed = JsonDocument.Parse(document);
        parsed.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
    }
}

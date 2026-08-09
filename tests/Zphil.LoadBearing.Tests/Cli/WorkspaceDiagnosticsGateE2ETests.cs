using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Roslyn.MsBuild;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>check</c>.</summary>
/// <remarks>
///     Three parts, all against the real MyApp fixture (each opens a workspace, hence <c>Serial</c>):
///     <list type="bullet">
///         <item>
///             <b>Fail closed.</b> A workspace-load failure means the model is incomplete, so
///             <c>check</c> exits 2 by default (overriding the clean 0 and the violated 1) rather than read
///             green on a partial model; <c>--allow-workspace-diagnostics</c> restores the prior 0/1 exit
///             with the load failures printed as warnings. Driven through <see cref="CheckRunner" /> with an
///             injected source that wraps the real cold load and adds synthetic load diagnostics — the one
///             way to exercise the gate without a genuinely broken project. <c>--json</c> stdout stays pure,
///             and a <c>--sarif</c> report written before the gate returns records the unsuccessful
///             invocation with the load diagnostics as notifications.
///         </item>
///         <item>
///             <b>Merge notes never gate.</b> A real same-FQN cross-project conflation (Shared.Widget
///             declared by two projects that do not reference each other) renders on the same diagnostics
///             stream — <c>warning:</c> on stderr, the <c>workspaceDiagnostics</c> array in JSON — while the
///             exit stays 0: the advisory notes are kept out of the fail-closed gate by construction.
///         </item>
///         <item>
///             <b>NuGetAudit advisories never gate.</b> An injected advisory in the codeless shape Roslyn
///             delivers — external publication timing, not a broken model — renders on the same stream (the
///             <c>warning:</c> line, the
///             <c>workspaceDiagnostics</c> array, a SARIF notification) while the exit stays 0/1:
///             <see cref="NuGetAuditDiagnostics" /> carves the family out of the gate input, yet a genuine
///             load failure riding alongside it still fails closed.
///         </item>
///     </list>
/// </remarks>
[Collection("Serial")]
public sealed class WorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    // A NuGetAudit advisory in the shape MSBuildWorkspace actually delivers one, captured from a run over a
    // solution referencing System.Security.Cryptography.Xml 4.7.0: Roslyn's project-load frame, package +
    // version, severity, GHSA URL — and no NU1902 anywhere, because Roslyn records BuildEventArgs.Message
    // and never .Code. This constant used to carry the code, which is why the gate could pass this test and
    // still red every real solution with a vulnerable package (issue #19).
    // The project path is written with forward slashes — the shape a non-Windows load produces — so the
    // constant can be asserted against JSON and SARIF payloads verbatim, without backslash escaping
    // standing between the test and the advisory text that is the actual subject.
    private const string AuditDiagnostic =
        "Msbuild failed when processing the file '/src/App/App.csproj' with message: Package "
        + "'System.Security.Cryptography.Xml' 4.7.0 has a known moderate severity vulnerability, "
        + "https://github.com/advisories/GHSA-vh55-786g-wjwj";

    private const string GateLine =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so check "
        + "cannot pass. Pass --allow-workspace-diagnostics to check against the partial model anyway.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Workspace-load diagnostics fail check closed ──────────────────────────────────────────────────

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_NoFlag_FailsClosedWithExitTwoAndGateLine()
    {
        // The clean spec would exit 0, but a project failed to load — the gate overrides that verdict.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false);

        result.ShouldRefuseWith();
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
        result.Err.ShouldContain(GateLine);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_WithFlag_RestoresPriorExitAndSuppressesGateLine()
    {
        // The escape hatch: the operator opts into the partial model, so the clean spec exits 0 as before.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, true, false);

        result.ShouldSucceed();
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the warning still renders
        result.Err.ShouldNotContain("error: the model is incomplete"); // but the gate did not fire
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_ViolatedSpecNoFlag_GateTakesPrecedenceOverExitOne()
    {
        // The violated spec would exit 1; the incomplete-model gate takes precedence and exits 2.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.ViolatedSpecDll, false, false);

        result.ShouldRefuseWith(GateLine);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_NoFlag_StdoutStaysPureJsonAndGateLineOnStderr()
    {
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, true);

        result.ShouldRefuseWith();
        // stdout is pure JSON: the diagnostic rides in the workspaceDiagnostics array and the gate line
        // never leaks onto stdout, so a hook can still parse the document.
        using JsonDocument _ = result.ShouldHaveJsonStdout();
        result.Out.ShouldContain("\"workspaceDiagnostics\"");
        result.Out.ShouldContain(LoadDiagnostic);
        result.Out.ShouldNotContain("error: the model is incomplete");
        // the gate line goes to stderr.
        result.Err.ShouldContain(GateLine);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticSarif_RecordsExecutionUnsuccessful()
    {
        // SARIF is written before the fail-closed gate returns, so the incomplete-model verdict reaches code
        // scanning: the invocation records executionSuccessful: false and the load diagnostic rides as a
        // tool-execution notification, even though the run exits 2.
        string sarifPath = Path.Combine(Path.GetTempPath(), $"loadbearing-sarif-{Guid.NewGuid():N}.sarif");
        try
        {
            CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false, sarifPath);

            result.ShouldRefuseWith();
            string sarif = File.ReadAllText(sarifPath);
            sarif.ShouldContain("\"executionSuccessful\": false");
            sarif.ShouldContain("\"toolExecutionNotifications\"");
            sarif.ShouldContain(LoadDiagnostic);
        }
        finally
        {
            File.Delete(sarifPath);
        }
    }

    // ── The MSBuild selection rides both surfaces, but only when something failed ──────────────────────

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_NamesTheMsBuildSelectionAndTheOverrideVariable()
    {
        // "A project failed to load" is nearly always a question about which MSBuild opened it, and until
        // this line existed the answer was unobtainable: MsBuildBootstrap described its choice in four
        // places and printed it in none, so a load failure arrived as a bare exit code.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false);

        result.Err.ShouldContain(
            $"warning: MSBuild for this run: {MsBuildBootstrap.LastSelection}.",
            customMessage: result.Err);
        result.Err.ShouldContain(
            "Set LOADBEARING_VS_INSTALL_PATH to a Visual Studio install root", customMessage: result.Err);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_LandsTheMsBuildNoteInTheDocumentBesideStderr()
    {
        // stderr is the CLI's channel and the MCP surface discards it, so the note only reaches a client if
        // it rides the document. It does, because it is composed into the one list both renderers read
        // rather than appended at write time — the whole of the MCP-side fix, seen from the CLI.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, true);

        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        WorkspaceDiagnosticsOf(document).ShouldBe([LoadDiagnostic, WorkspaceDiagnosticsRenderer.MsBuildNote()]);
        result.Err.ShouldContain("MSBuild for this run:"); // and it is still on stderr, unchanged
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnosticJson_ComposesTheNoteWithoutFlippingTheGateVerdict()
    {
        // The regression composition could introduce, pinned shut. The composed list carries the MSBuild
        // note, which is not a NuGetAudit advisory — so a composed list reaching IncompleteModelGate would
        // read as a load failure, mark every run incomplete, and exit 2 always. The gate reads
        // source.Diagnostics; only the renderers read the composition.
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        WorkspaceDiagnosticsOf(document).ShouldBe([AuditDiagnostic, WorkspaceDiagnosticsRenderer.MsBuildNote()]);
        document.RootElement.TryGetProperty("modelIncomplete", out _).ShouldBeFalse();
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_NoWorkspaceDiagnostics_LeavesTheMsBuildNoteUnprinted()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run says nothing about MSBuild on any stream.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll);

        result.ShouldSucceed();
        result.Err.ShouldNotContain("MSBuild for this run");
        result.Out.ShouldNotContain("MSBuild for this run");
    }

    // ── Same-FQN merge notes render but never gate ────────────────────────────────────────────────────

    [Fact]
    public async Task Check_SameFqnAcrossTwoProjects_RendersMergeNoteWarningWithoutTrippingGate()
    {
        using var workspace = new TempFixtureWorkspace();
        WriteCollidingType(workspace);

        CliResult result = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");

        result.ShouldSucceed(); // merge notes are advisory — the gate never fires on them
        result.Err.ShouldContain(
            "warning: Type 'Shared.Widget' is declared by projects 'MyApp.Domain' and 'MyApp.Legacy.Billing'");
        result.Err.ShouldContain("arch.Project('MyApp.Legacy.Billing') selections will not include it.");
        result.Err.ShouldNotContain("error: the model is incomplete"); // the fail-closed gate did NOT fire
    }

    [Fact]
    public async Task Check_SameFqnAcrossTwoProjectsJson_LandsMergeNoteInWorkspaceDiagnosticsWithoutTrippingGate()
    {
        using var workspace = new TempFixtureWorkspace();
        WriteCollidingType(workspace);

        CliResult result = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        result.ShouldSucceed("\"workspaceDiagnostics\"");
        result.Out.ShouldContain(
            "Type 'Shared.Widget' is declared by projects 'MyApp.Domain' and 'MyApp.Legacy.Billing'");
        result.Out.ShouldNotContain("error: the model is incomplete");
    }

    // ── NuGetAudit advisories render but never gate ───────────────────────────────────────────────────

    [Fact]
    public async Task Check_NuGetAuditDiagnostic_NoFlag_RendersWarningWithoutTrippingGate()
    {
        // A NuGetAudit advisory is external-timing noise, not a broken model — the clean spec still exits 0.
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, false);

        result.ShouldSucceed();
        result.Err.ShouldContain($"warning: {AuditDiagnostic}"); // the advisory still renders as a warning
        result.Err.ShouldNotContain("error: the model is incomplete"); // but the gate never fired
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnostic_ViolatedSpecNoFlag_ExitsOneAndDoesNotGate()
    {
        // Filtering the advisory out of the gate must not mask a real violation — the violated spec exits 1.
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.ViolatedSpecDll, false, false);

        result.ShouldReportViolations();
        result.Err.ShouldNotContain("error: the model is incomplete"); // the fail-closed gate did NOT fire
    }

    [Fact]
    public async Task Check_NuGetAuditPlusLoadFailure_NoFlag_StillFailsClosed()
    {
        // An advisory riding alongside a genuine load failure: the load failure still gates, and both render.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [AuditDiagnostic, LoadDiagnostic], CliRunner.CleanSpecDll, false, false);

        result.ShouldRefuseWith(GateLine);
        result.Err.ShouldContain($"warning: {AuditDiagnostic}"); // the advisory renders
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // and so does the load failure — no over-filtering
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnosticJson_NoFlag_ExitsCleanWithAdvisoryInWorkspaceDiagnostics()
    {
        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed();
        // stdout stays pure JSON: the advisory rides the workspaceDiagnostics array and no gate line leaks.
        using JsonDocument _ = result.ShouldHaveJsonStdout();
        result.Out.ShouldContain("\"workspaceDiagnostics\"");
        result.Out.ShouldContain(AuditDiagnostic);
        result.Out.ShouldNotContain("error: the model is incomplete");
        result.Err.ShouldNotContain("error: the model is incomplete"); // no gate line anywhere
    }

    [Fact]
    public async Task Check_NuGetAuditDiagnosticSarif_RecordsExecutionSuccessful()
    {
        // SARIF records a successful invocation: the advisory rides as a tool-execution notification but,
        // filtered out of the gate, it does not flip executionSuccessful — the run exits 0.
        string sarifPath = Path.Combine(Path.GetTempPath(), $"loadbearing-sarif-{Guid.NewGuid():N}.sarif");
        try
        {
            CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, false, sarifPath);

            result.ShouldSucceed();
            string sarif = File.ReadAllText(sarifPath);
            sarif.ShouldContain("\"executionSuccessful\": true");
            sarif.ShouldContain("\"toolExecutionNotifications\"");
            sarif.ShouldContain(AuditDiagnostic);
        }
        finally
        {
            File.Delete(sarifPath);
        }
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static string[] WorkspaceDiagnosticsOf(JsonDocument document)
    {
        return document.RootElement.GetProperty("workspaceDiagnostics")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }

    private static Task<CliResult> RunWithInjectedDiagnosticAsync(
        string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null)
    {
        return RunWithInjectedDiagnosticAsync([LoadDiagnostic], spec, allowWorkspaceDiagnostics, json, sarif);
    }

    private static async Task<CliResult> RunWithInjectedDiagnosticAsync(
        IReadOnlyList<string> diagnostics, string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string solution = CliRunner.MyAppSolution;
        var runner = new CheckRunner(output, error, new DiagnosticInjectingSolutionSource(diagnostics), new FakeEnvironment());

        int exit = await runner.RunAsync(
            new CheckRequest(
                solution, spec, json, null, Path.GetDirectoryName(Path.GetFullPath(solution))!, true, null,
                allowWorkspaceDiagnostics, sarif),
            Ct);

        return new CliResult(exit, output.ToString(), error.ToString());
    }

    // Declares Shared.Widget in two projects that do not reference each other (Domain → Web → Billing, so
    // Domain and Billing share no edge), producing a real same-FQN cross-project conflation with no compile
    // conflict — nothing uses Widget, so no use site is ambiguous. Ordinal project order makes Domain the
    // first declarer (winner) and Legacy.Billing the loser.
    private static void WriteCollidingType(TempFixtureWorkspace workspace)
    {
        const string widget = "namespace Shared;\npublic class Widget { }\n";
        File.WriteAllText(workspace.PathOf("MyApp.Domain", "Widget.cs"), widget);
        File.WriteAllText(workspace.PathOf("MyApp.Legacy.Billing", "Widget.cs"), widget);
    }
}

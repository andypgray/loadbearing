using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The workspace-diagnostics contract on <c>check</c>. Two halves, both against the real MyApp
///     fixture (each opens a workspace, hence <c>Serial</c>):
///     <list type="bullet">
///         <item>
///             <b>Fail closed.</b> A workspace-load failure means the model is incomplete, so
///             <c>check</c> exits 2 by default (overriding the clean 0 and the violated 1) rather than read
///             green on a partial model; <c>--allow-workspace-diagnostics</c> restores the prior 0/1 exit
///             with the load failures printed as warnings. Driven through <see cref="CheckRunner" /> with an
///             injected source that wraps the real cold load and adds synthetic load diagnostics — the one
///             way to exercise the gate without a genuinely broken project. <c>--json</c> stdout stays pure.
///         </item>
///         <item>
///             <b>Merge notes never gate.</b> A real same-FQN cross-project conflation (Shared.Widget
///             declared by two projects that do not reference each other) renders on the same diagnostics
///             stream — <c>warning:</c> on stderr, the <c>workspaceDiagnostics</c> array in JSON — while the
///             exit stays 0: the advisory notes are kept out of the fail-closed gate by construction.
///         </item>
///     </list>
/// </summary>
[Collection("Serial")]
public sealed class WorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

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

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
        result.Err.ShouldContain(GateLine);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_WithFlag_RestoresPriorExitAndSuppressesGateLine()
    {
        // The escape hatch: the operator opts into the partial model, so the clean spec exits 0 as before.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, true, false);

        result.Exit.ShouldBe(0);
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the warning still renders
        result.Err.ShouldNotContain("error: the model is incomplete"); // but the gate did not fire
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_ViolatedSpecNoFlag_GateTakesPrecedenceOverExitOne()
    {
        // The violated spec would exit 1; the incomplete-model gate takes precedence and exits 2.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.ViolatedSpecDll, false, false);

        result.Exit.ShouldBe(2);
        result.Err.ShouldContain(GateLine);
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_NoFlag_StdoutStaysPureJsonAndGateLineOnStderr()
    {
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, true);

        result.Exit.ShouldBe(2);
        // stdout is pure JSON: the diagnostic rides in the workspaceDiagnostics array and the gate line
        // never leaks onto stdout, so a hook can still parse the document.
        result.Out.Trim().ShouldStartWith("{");
        result.Out.ShouldContain("\"workspaceDiagnostics\"");
        result.Out.ShouldContain(LoadDiagnostic);
        result.Out.ShouldNotContain("error: the model is incomplete");
        // the gate line goes to stderr.
        result.Err.ShouldContain(GateLine);
    }

    // ── Same-FQN merge notes render but never gate ────────────────────────────────────────────────────

    [Fact]
    public async Task Check_SameFqnAcrossTwoProjects_RendersMergeNoteWarningWithoutTrippingGate()
    {
        using var workspace = new TempFixtureWorkspace();
        WriteCollidingType(workspace);

        CliResult result = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");

        result.Exit.ShouldBe(0); // merge notes are advisory — the gate never fires on them
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

        result.Exit.ShouldBe(0);
        result.Out.ShouldContain("\"workspaceDiagnostics\"");
        result.Out.ShouldContain(
            "Type 'Shared.Widget' is declared by projects 'MyApp.Domain' and 'MyApp.Legacy.Billing'");
        result.Out.ShouldNotContain("error: the model is incomplete");
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<CliResult> RunWithInjectedDiagnosticAsync(string spec, bool allowWorkspaceDiagnostics, bool json)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string solution = CliRunner.MyAppSolution;
        var runner = new CheckRunner(output, error, new DiagnosticInjectingSolutionSource([LoadDiagnostic]), new FakeEnvironment());

        int exit = await runner.RunAsync(
            new CheckRequest(
                solution, spec, json, null, Path.GetDirectoryName(Path.GetFullPath(solution))!, true, null,
                allowWorkspaceDiagnostics),
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

    // Wraps a real cold load, then re-wraps the handle with synthetic workspace-load diagnostics — the real
    // MyApp fixture loads cleanly, so this is the only way to drive the fail-closed gate without a broken project.
    // The real handle rides as the owned disposable, so disposing this wrapper's handle disposes the workspace.
    private sealed class DiagnosticInjectingSolutionSource(IReadOnlyList<string> diagnostics) : ISolutionSource
    {
        private readonly ColdSolutionSource inner = new();

        public async Task<SolutionHandle> AcquireAsync(string? solution, string workingDirectory, CancellationToken ct)
        {
            SolutionHandle real = await inner.AcquireAsync(solution, workingDirectory, ct);
            return new SolutionHandle(real.Solution, real.SolutionPath, diagnostics, real, real.WarmFragments);
        }
    }
}
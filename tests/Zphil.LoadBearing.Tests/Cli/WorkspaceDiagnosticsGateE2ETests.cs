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
///     <para>
///         Six parts, all against the real MyApp fixture (each opens a workspace, hence <c>Serial</c>).
///         Driven through <see cref="CheckRunner" /> with an injected source that wraps the real cold load
///         and adds synthetic diagnostics, synthetic failed projects, synthetic restore-failed ones and/or
///         synthetic unchecked ones — the one way to exercise the gate without a genuinely broken project,
///         and the one way to put the inputs in front of it <em>separately</em>, which is the whole subject
///         of the last three parts below.
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Fail closed.</b> A project that failed to load means the model is incomplete, so
///             <c>check</c> exits 2 by default (overriding the clean 0 and the violated 1) rather than read
///             green on a partial model; <c>--allow-workspace-diagnostics</c> restores the prior 0/1 exit
///             with the load failures printed as warnings. <c>--json</c> stdout stays pure, and a
///             <c>--sarif</c> report written before the gate returns records the unsuccessful invocation
///             with the load diagnostics as notifications.
///         </item>
///         <item>
///             <b>Merge notes never gate.</b> A real same-FQN cross-project conflation (Shared.Widget
///             declared by two projects that do not reference each other) renders on the same diagnostics
///             stream — <c>warning:</c> on stderr, the <c>workspaceDiagnostics</c> array in JSON — while the
///             exit stays 0: the advisory notes ride a separate slot by construction.
///         </item>
///         <item>
///             <b>NuGetAudit advisories never gate.</b> An injected advisory in the codeless shape Roslyn
///             delivers — external publication timing, not a broken model — renders on the same stream (the
///             <c>warning:</c> line, the <c>workspaceDiagnostics</c> array, a SARIF notification) while the
///             exit stays 0/1, and a genuine load failure riding alongside it still fails closed.
///         </item>
///         <item>
///             <b>No message gates, in any language.</b> The measured field defects, replayed verbatim: a
///             NuGet pruning advisory (<c>NU1510</c>) that refused a solution whose only rule passed, and an
///             audit-fetch failure (<c>NU1900</c>) in German that refused where the identical English run
///             exits 0. Both now render and exit 0, because a diagnostic is no longer an input to the
///             decision — which is what makes the fix language-independent rather than one more phrase in a
///             matcher.
///         </item>
///         <item>
///             <b>A narrowed universe scopes, never gates.</b> Unchecked projects injected on their own put
///             <c>uncheckedProjects</c> in the document and one warning-level notification in the SARIF while
///             the clean spec still exits 0 and neither incomplete-model slot appears — the separation the
///             product makes between a model that is wrong and one that is merely smaller.
///         </item>
///         <item>
///             <b>A failed NuGet restore gates on the same terms, in its own words.</b> The project loaded
///             completely, so nothing in the loaded structure blames it — but its package edges are missing,
///             which was measured to turn a failing rule green. It exits 2 with a lede naming restore rather
///             than the build, takes the same opt-out, and when both causes hold the load block leads.
///         </item>
///     </list>
/// </remarks>
[Collection("Serial")]
public sealed class WorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    // The project the gate is told failed to load, and the only thing that makes it fire. Injected apart
    // from the diagnostic above because the product separates them: the diagnostic renders, this decides.
    private const string BrokenProject = "C:/repo/MyApp.Broken/MyApp.Broken.csproj";

    // The project the run is told a solution filter left out. The third input, separate again: this one
    // neither renders as a warning nor decides anything — it scopes the answer.
    private const string UncheckedProject = "C:/repo/MyApp.Skipped/MyApp.Skipped.csproj";

    // The project the gate is told restored badly — the fourth input, and the second that decides. It loaded
    // completely, which is exactly why it needs its own slot: nothing about the loaded solution says so.
    private const string RestoreFailedProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    // The NU1510 pruning advisory, captured verbatim from a restore of a net10.0 project referencing a
    // package the shared framework now carries. It is an ordinary restore warning, it says nothing about
    // whether the model built, and it refused a solution whose one rule passed — the defect this class's
    // fourth part exists to keep closed. .NET 10 emits it for a large and growing set of packages.
    private const string PruningAdvisory =
        "Msbuild failed when processing the file '/src/App/App.csproj' with message: PackageReference "
        + "System.Text.Encodings.Web will not be pruned. Consider removing this package from your "
        + "dependencies, as it is likely unnecessary.";

    // The same NU1510, in German. Nothing in the product reads either spelling.
    private const string GermanPruningAdvisory =
        "Msbuild failed when processing the file '/src/App/App.csproj' with message: PackageReference "
        + "System.Text.Encodings.Web wird nicht gekürzt. Erwägen Sie, dieses Paket aus Ihren Abhängigkeiten "
        + "zu entfernen, da es wahrscheinlich nicht erforderlich ist.";

    // The NU1900 audit-fetch failure in German, captured verbatim from a restore against an unreachable feed
    // under DOTNET_CLI_UI_LANGUAGE=de. The hardest case in the family and the reason a text matcher could
    // never be finished: no GHSA URL, no NU1900 token, and neither English phrase the old matcher keyed on.
    private const string GermanAuditFetchFailure =
        "Msbuild failed when processing the file '/src/App/App.csproj' with message: Fehler beim Abrufen von "
        + "Paketsicherheitsrisikodaten: Der Dienstindex für die Quelle "
        + "\"https://nuget.fieldtest.invalid/v3/index.json\" konnte nicht geladen werden.";

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
        "error: the model is incomplete — 1 project failed to load, so check cannot pass:";

    private const string RestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 1 project, so check cannot pass: "
        + "package references that resolved to nothing produce no edges, so a rule about a package is "
        + "measured against a model that never saw it:";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Workspace-load diagnostics fail check closed ──────────────────────────────────────────────────

    [Fact]
    public async Task Check_WorkspaceLoadDiagnostic_NoFlag_FailsClosedWithExitTwoAndGateLine()
    {
        // The clean spec would exit 0, but a project failed to load — the gate overrides that verdict.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false);

        result.ShouldRefuseWith(GateLine, BrokenProject); // and the refusal names what failed
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
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

        result.ShouldRefuseWith(GateLine, BrokenProject);
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
        using TempDirectory temp = TestTempRoot.Fresh("gate-sarif");
        string sarifPath = temp.PathOf("gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain("\"executionSuccessful\": false");
        sarif.ShouldContain("\"toolExecutionNotifications\"");
        sarif.ShouldContain(LoadDiagnostic);
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
        WorkspaceDiagnosticsOf(document)
            .ShouldBe([LoadDiagnostic, MsBuildBootstrap.SelectionNote()]);
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
        WorkspaceDiagnosticsOf(document)
            .ShouldBe([AuditDiagnostic, MsBuildBootstrap.SelectionNote()]);
        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
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
            [AuditDiagnostic, LoadDiagnostic], CliRunner.CleanSpecDll, false, false, null, [BrokenProject]);

        result.ShouldRefuseWith(GateLine, BrokenProject);
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
        using TempDirectory temp = TestTempRoot.Fresh("gate-sarif");
        string sarifPath = temp.PathOf("gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync([AuditDiagnostic], CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldSucceed();
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain("\"executionSuccessful\": true");
        sarif.ShouldContain("\"toolExecutionNotifications\"");
        sarif.ShouldContain(AuditDiagnostic);
    }

    // ── no message gates, in any language ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PruningAdvisory)]
    [InlineData(GermanPruningAdvisory)]
    [InlineData(GermanAuditFetchFailure)]
    public async Task Check_RestoreWarningWithNoFailedProject_RendersItAndExitsClean(string diagnostic)
    {
        // The two measured field refusals, in three spellings, all of which used to reach a decision. None of
        // them says a project failed to build — they are restore warnings about packages and feeds — and
        // nothing loads them into the gate any more, so the clean spec exits 0 and the warning still prints.
        // The German pair is the point of the theory: recognition cannot depend on a phrase, because NuGet
        // ships its wording in many languages and none of it is a contract.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [diagnostic], CliRunner.CleanSpecDll, false, false);

        result.ShouldSucceed();
        result.Err.ShouldContain($"warning: {diagnostic}");
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_RestoreWarningWithNoFailedProjectJson_StampsNoneOfTheWorkspaceVerdictSlots()
    {
        // The document half. A restore warning is data worth carrying, but it is not a verdict: an
        // arch_check client reading modelIncomplete must not be told the answer is untrustworthy because a
        // package could stand to be removed from a csproj. uncheckedProjects rides here too, because absent
        // is what every unfiltered run must render — a slot present-but-empty would move every golden.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [PruningAdvisory], CliRunner.CleanSpecDll, false, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("uncheckedProjects", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("restoreFailedProjects", out _)
            .ShouldBeFalse();
        result.Out.ShouldContain("will not be pruned"); // it still rides workspaceDiagnostics
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticJson_CarriesTheFailedProjectsBesideTheVerdict()
    {
        // What modelIncomplete cannot say on its own: which projects are missing. The diagnostics array
        // cannot be read for it — MSBuild's words about a fatal failure and about a restore warning arrive
        // in the same shape — so the evidence needs its own slot, and it is the only channel an MCP client
        // has for it.
        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, true);

        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
        // Solution-relative and forward-slashed, like every other path in the document, so no machine path
        // ever lands in one a golden pins.
        var failedProjects = document.RootElement.GetProperty("failedProjects")
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToList();
        failedProjects.ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Broken/MyApp.Broken.csproj");
    }

    // ── a narrowed universe rides the document and the SARIF, and gates neither ────────────────────────

    [Fact]
    public async Task Check_NarrowedUniverseJson_CarriesTheUncheckedProjectsWithoutMarkingTheModelIncomplete()
    {
        // The channel the human stamp can never reach: --json owns stdout, so the surface an agent reads is
        // the one with no stamp on it. What rides it must scope the verdict and not overturn it — the rules
        // all ran and all answered — so the clean spec still exits 0 and neither incomplete-model slot
        // appears beside it.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, true, uncheckedProjects: [UncheckedProject]);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        // Solution-relative and forward-slashed, like failedProjects beside it and every other path here.
        var uncheckedProjects = document.RootElement.GetProperty("uncheckedProjects")
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToList();
        uncheckedProjects.ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Skipped/MyApp.Skipped.csproj");
    }

    [Fact]
    public async Task Check_NarrowedUniverseSarif_AddsOneWarningNotificationToASuccessfulInvocation()
    {
        // Code scanning has no exit code and no stdout: a clean SARIF over a filter would close every alert
        // the unchecked projects would have raised. Warning rather than error, and executionSuccessful stays
        // true, because the results are true — they are simply not the whole solution's.
        using TempDirectory temp = TestTempRoot.Fresh("narrowed-sarif");
        string sarifPath = temp.PathOf("narrowed.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, false, sarifPath, uncheckedProjects: [UncheckedProject]);

        result.ShouldSucceed();
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain("\"executionSuccessful\": true");
        sarif.ShouldContain("\"level\": \"warning\"");
        sarif.ShouldContain(
            "A solution filter narrowed this run: 1 project the solution declares was not checked, so these "
            + "results cover part of the solution: ");
        sarif.ShouldContain("MyApp.Skipped/MyApp.Skipped.csproj");
    }

    // ── a failed NuGet restore gates on the same terms as a failed load ───────────────────────────────

    [Fact]
    public async Task Check_RestoreFailedProject_NoFlag_FailsClosedWithItsOwnGateLine()
    {
        // The measured silent pass, closed. The project loaded completely — full document set, both output
        // paths — so nothing in the loaded structure blames it and the old gate saw a clean solution, while
        // every package edge it declares was missing from the model and a rule over one reported itself inert.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, false, false);

        result.ShouldRefuseWith(RestoreGateLine, RestoreFailedProject);
    }

    [Fact]
    public async Task Check_RestoreFailedProject_NamesRestoreRatherThanBuildAsTheRemedy()
    {
        // The whole reason this is a second slot and not an extra entry in failedProjects: the repair differs.
        // A reader sent to `dotnet build` for a feed that could not be reached learns nothing.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, false, false);

        result.Err.ShouldContain("Restore the solution (dotnet restore)");
        // The remedy leads and the warnings are offered conditionally, because this block now also covers a
        // restore that never ran — which wrote no NuGet logs for the SDK to replay above it.
        result.Err.ShouldContain("any NuGet errors behind a restore that ran are in the warnings above");
        result.Err.ShouldNotContain("failed to load"); // and it does not claim the project failed to load
    }

    [Fact]
    public async Task Check_RestoreFailedProject_WithFlag_RestoresPriorExit()
    {
        // The same escape hatch, deliberately: one flag for one question — is a partial model acceptable —
        // rather than one flag per way the model can come up short.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, true, false);

        result.ShouldSucceed();
        result.Err.ShouldNotContain("error: the model is incomplete");
    }

    [Fact]
    public async Task Check_RestoreFailedProject_ViolatedSpecNoFlag_GateTakesPrecedenceOverExitOne()
    {
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.ViolatedSpecDll, false, false);

        result.ShouldRefuseWith(RestoreGateLine, RestoreFailedProject);
    }

    [Fact]
    public async Task Check_RestoreFailedProjectJson_CarriesTheRestoreFailedProjectsBesideTheVerdict()
    {
        // Its own slot, not a second entry in failedProjects: these projects loaded, so calling them failed
        // loads would be false, and the remedy a client should surface is a different command. Solution-relative
        // and forward-slashed, like every other path in the document.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, false, true);

        result.ShouldRefuseWith();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
        document.RootElement.TryGetProperty("failedProjects", out _)
            .ShouldBeFalse();
        ProjectsAt(document, "restoreFailedProjects")
            .ShouldHaveSingleItem()
            .ShouldEndWith("MyApp.Unrestored/MyApp.Unrestored.csproj");
    }

    [Fact]
    public async Task Check_RestoreFailedProjectJsonWithTheOptOut_StillStampsTheModelIncomplete()
    {
        // The opt-out changes the exit code, never the truth about the model — which is the whole reason the
        // document stamps a fact rather than echoing a verdict.
        CliResult result = await RunWithInjectedRestoreFailureAsync(CliRunner.CleanSpecDll, true, true);

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.GetProperty("modelIncomplete")
            .GetBoolean()
            .ShouldBeTrue();
        ProjectsAt(document, "restoreFailedProjects")
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Check_RestoreFailedProjectSarif_RecordsExecutionUnsuccessfulAndNamesTheCause()
    {
        // Code scanning has no exit code, so the one channel that can carry a refusal there is
        // executionSuccessful — which follows the gate rather than the cause, and so needed no edit. The
        // notification is what needed adding: until it existed the cause reached SARIF only as MSBuild's
        // replayed prose, where a fatal failure and a restore warning are the same shape.
        using TempDirectory temp = TestTempRoot.Fresh("restore-gate-sarif");
        string sarifPath = temp.PathOf("restore-gate.sarif");

        CliResult result = await RunWithInjectedRestoreFailureAsync(
            CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain("\"executionSuccessful\": false");
        sarif.ShouldContain(
            "The model is incomplete: NuGet packages did not resolve for 1 project, so these results were "
            + "reached against a codebase whose package references resolved to nothing: ");
        sarif.ShouldContain("MyApp.Unrestored/MyApp.Unrestored.csproj");
        sarif.ShouldContain("\"level\": \"error\"");
    }

    [Fact]
    public async Task Check_WorkspaceLoadDiagnosticSarif_NamesTheFailedProjectsToo()
    {
        // The gap this closed on the way past: failedProjects had no SARIF notification either, so the one
        // surface that names a cause could name neither. Both are added together rather than leaving the
        // channel able to explain half an incomplete model.
        using TempDirectory temp = TestTempRoot.Fresh("load-gate-sarif");
        string sarifPath = temp.PathOf("load-gate.sarif");

        CliResult result = await RunWithInjectedDiagnosticAsync(CliRunner.CleanSpecDll, false, false, sarifPath);

        result.ShouldRefuseWith();
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain(
            "The model is incomplete: 1 project failed to load, so these results were reached against a "
            + "codebase missing whole projects: ");
        sarif.ShouldContain("MyApp.Broken/MyApp.Broken.csproj");
    }

    [Fact]
    public async Task Check_LoadFailureAndRestoreFailure_WritesTheLoadBlockFirst()
    {
        // Both causes at once. A project that never loaded is more fundamentally broken than one that loaded
        // without its packages, and the two ask for different repairs, so the run is told about both — in that
        // order, and never merged into one list that would name the wrong remedy for half of it.
        CliResult result = await RunWithInjectedDiagnosticAsync(
            [], CliRunner.CleanSpecDll, false, false, null, [BrokenProject],
            restoreFailedProjects: [RestoreFailedProject]);

        result.ShouldRefuseWith(GateLine, BrokenProject, RestoreGateLine, RestoreFailedProject);
        result.Err.IndexOf(RestoreGateLine, StringComparison.Ordinal)
            .ShouldBeGreaterThan(result.Err.IndexOf(GateLine, StringComparison.Ordinal));
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static string[] WorkspaceDiagnosticsOf(JsonDocument document)
    {
        return document.RootElement.GetProperty("workspaceDiagnostics")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();
    }

    private static List<string> ProjectsAt(JsonDocument document, string slot)
    {
        return document.RootElement.GetProperty(slot)
            .EnumerateArray()
            .Select(element => element.GetString() ?? "")
            .ToList();
    }

    // The load-failure shape: the diagnostic renders, and the project beside it is what gates.
    private static Task<CliResult> RunWithInjectedDiagnosticAsync(
        string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null)
    {
        return RunWithInjectedDiagnosticAsync(
            [LoadDiagnostic], spec, allowWorkspaceDiagnostics, json, sarif, [BrokenProject]);
    }

    // The restore-failure shape: no diagnostic at all, because the SDK's replay of NuGet's own logs is not
    // this product's to synthesize — the project beside it is the whole gate input.
    private static Task<CliResult> RunWithInjectedRestoreFailureAsync(
        string spec, bool allowWorkspaceDiagnostics, bool json, string? sarif = null)
    {
        return RunWithInjectedDiagnosticAsync(
            [], spec, allowWorkspaceDiagnostics, json, sarif,
            restoreFailedProjects: [RestoreFailedProject]);
    }

    private static async Task<CliResult> RunWithInjectedDiagnosticAsync(
        IReadOnlyList<string> diagnostics, string spec, bool allowWorkspaceDiagnostics, bool json,
        string? sarif = null, IReadOnlyList<string>? failedProjects = null,
        IReadOnlyList<string>? uncheckedProjects = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string solution = CliRunner.MyAppSolution;
        var runner = new CheckRunner(
            output, error,
            new DiagnosticInjectingSolutionSource(
                diagnostics, failedProjects, uncheckedProjects, restoreFailedProjects),
            new FakeEnvironment());

        int exit = await runner.RunAsync(
            new CheckRequest(
                solution, spec, json, null, Path.GetDirectoryName(Path.GetFullPath(solution))!, true, null,
                allowWorkspaceDiagnostics, sarif, null, DocumentGrain.Full),
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

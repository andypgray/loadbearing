using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>baseline</c> — the second command of issue #19.</summary>
/// <remarks>
///     <para>
///         The sequence a team actually runs is <c>check</c> then <c>baseline --init</c>. <c>check</c> has
///         failed closed on a partial model since the gate existed; <c>baseline</c> used to render the same
///         warnings, carry on, write a baseline from that partial model, and exit 0. That is the worst of
///         the three answers, because a baseline is the team's signature on its debt: unloaded projects
///         declare no types, so <c>--init</c> captures "zero debt" for rules that were never measured, and
///         every later ratchet reading is taken against that signature.
///     </para>
///     <para>
///         Driven through <see cref="BaselineRunner" /> with a
///         <see cref="DiagnosticInjectingSolutionSource" /> over a private copy of the MyApp fixture — the
///         fixture loads cleanly by design, so injection is the only way to put a load failure in front of
///         a command that then writes files. The genuinely-partial load lives in
///         <see cref="PartialLoadWorkspaceE2ETests" />; what this class owns is the write.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class BaselineWorkspaceDiagnosticsGateE2ETests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    // A NuGetAudit advisory in the codeless shape Roslyn actually delivers one (see
    // WorkspaceDiagnosticsGateE2ETests for the provenance): external publication timing, not a broken model.
    private const string AuditDiagnostic =
        "Msbuild failed when processing the file '/src/App/App.csproj' with message: Package "
        + "'System.Security.Cryptography.Xml' 4.7.0 has a known moderate severity vulnerability, "
        + "https://github.com/advisories/GHSA-vh55-786g-wjwj";

    // The project the gate is told failed to load. Injected separately from the diagnostic above, because
    // the product now separates them: the diagnostic renders and the project decides.
    private const string BrokenProject = "C:/repo/MyApp.Broken/MyApp.Broken.csproj";

    private const string GateLine =
        "error: the model is incomplete — 1 project failed to load, so no baseline was written: a baseline "
        + "captured from a partial model signs off debt that was never measured:";

    private const string CheckGateLine =
        "error: the model is incomplete — 1 project failed to load, so check cannot pass:";

    // The project the gate is told restored badly. It loaded completely, so only its assets file says so.
    private const string UnrestoredProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    private const string RestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 1 project, so no baseline was "
        + "written: every violation resting on a package edge is absent from this model, so the baseline "
        + "would sign off debt it could not see:";

    private static readonly string[] ConventionalBaselineFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];

    [Fact]
    public async Task CheckThenBaselineInit_WorkspaceLoadDiagnostic_BothFailClosedAndNothingIsWritten()
    {
        // The issue #19 sequence, in order. Its literal second command is what used to exit 0.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile); // uncaptured, so a run that reached the write would create it

        CliResult check = await RunCheckAsync(workspace, [LoadDiagnostic], false, [BrokenProject]);
        check.ShouldRefuseWith(CheckGateLine, BrokenProject);

        CliResult init = await RunBaselineAsync(workspace, [LoadDiagnostic], InitRequest, false, [BrokenProject]);

        init.ShouldRefuseWith(GateLine, BrokenProject);
        init.Err.ShouldContain($"warning: {LoadDiagnostic}"); // the load failure still prints as a warning
        init.Out.ShouldBeEmpty(); // it refused before the ratchet survey, so it reported no per-rule outcome
        File.Exists(baselineFile)
            .ShouldBeFalse(); // and above all, wrote nothing
    }

    [Fact]
    public async Task BaselineInit_WorkspaceLoadDiagnosticWithFlag_WritesTheBaselineAndStillWarns()
    {
        // The escape hatch, and the negative control for "wrote nothing" above: the operator opts into the
        // partial model, the warnings still print, and the capture happens.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(workspace, [LoadDiagnostic], InitRequest, true);

        init.ShouldSucceed();
        init.Err.ShouldContain($"warning: {LoadDiagnostic}");
        init.Err.ShouldNotContain("error: the model is incomplete");
        init.Out.ShouldContain("wrote");
        File.Exists(baselineFile)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task BaselineAcceptReductions_WorkspaceLoadDiagnostic_FailsClosedBeforeShrinkingAnything()
    {
        // The dangerous mode. --accept-reductions removes entries whose violation "no longer occurs" — and a
        // project that stopped loading looks exactly like a violation that stopped, so an ungated run would
        // shrink the ratchet on the strength of nothing at all. The checked-in baseline must survive byte
        // for byte.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult accept = await RunBaselineAsync(
            workspace, [LoadDiagnostic], AcceptReductionsRequest, false, [BrokenProject]);

        accept.ShouldRefuseWith(GateLine, BrokenProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineAdd_WorkspaceLoadDiagnostic_FailsClosedBeforeGrandfatheringAnything()
    {
        // The third mode gates too — uniformly, not per-mode. --add resolves the entry it grandfathers
        // against the current violations, so a partial model can make it refuse a real one or attribute the
        // wrong one; either way the ratchet grows against a model nobody checked.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult add = await RunBaselineAsync(workspace, [LoadDiagnostic], AddRequest, false, [BrokenProject]);

        add.ShouldRefuseWith(GateLine, BrokenProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineInit_NuGetAuditDiagnostic_WritesTheBaselineWithoutTrippingTheGate()
    {
        // Carve-out parity with check: an advisory is external-timing noise, not a broken model, so it
        // renders and does not gate. Getting this wrong on baseline is how issue #19 would return on a
        // different verb.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(workspace, [AuditDiagnostic], InitRequest, false);

        init.ShouldSucceed();
        init.Err.ShouldContain($"warning: {AuditDiagnostic}"); // the advisory still renders
        init.Err.ShouldNotContain("error: the model is incomplete");
        File.Exists(baselineFile)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task BaselineInit_NuGetAuditPlusLoadFailure_StillFailsClosed()
    {
        // The carve-out must not over-filter: an advisory riding alongside a genuine load failure still gates.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(
            workspace, [AuditDiagnostic, LoadDiagnostic], InitRequest, false, [BrokenProject]);

        init.ShouldRefuseWith(GateLine, BrokenProject);
        init.Err.ShouldContain($"warning: {AuditDiagnostic}");
        init.Err.ShouldContain($"warning: {LoadDiagnostic}");
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task BaselineInit_RestoreFailedProject_FailsClosedAndWritesNothing()
    {
        // The same signature, the other way to be missing what you are signing for. A baseline captured while
        // a project's packages resolved to nothing records every violation resting on a package edge as
        // absent — and the team has then signed off debt no run ever measured.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        File.Delete(baselineFile);

        CliResult init = await RunBaselineAsync(
            workspace, [], InitRequest, false, restoreFailedProjects: [UnrestoredProject]);

        init.ShouldRefuseWith(RestoreGateLine, UnrestoredProject);
        init.Out.ShouldBeEmpty();
        File.Exists(baselineFile)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task BaselineAcceptReductions_RestoreFailedProject_FailsClosedBeforeShrinkingAnything()
    {
        // The dangerous mode, on the cause that reaches it most quietly: a package that stopped resolving
        // looks exactly like a violation that stopped occurring, and --accept-reductions would delete the
        // real entry on the strength of it.
        using var workspace = new TempFixtureWorkspace();
        string baselineFile = workspace.PathOf(ConventionalBaselineFile);
        byte[] before = File.ReadAllBytes(baselineFile);

        CliResult accept = await RunBaselineAsync(
            workspace, [], AcceptReductionsRequest, false, restoreFailedProjects: [UnrestoredProject]);

        accept.ShouldRefuseWith(RestoreGateLine, UnrestoredProject);
        File.ReadAllBytes(baselineFile)
            .ShouldBe(before);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static BaselineRequest InitRequest(string solution, bool allowWorkspaceDiagnostics)
    {
        return Request(solution, allowWorkspaceDiagnostics) with { Init = true };
    }

    private static BaselineRequest AcceptReductionsRequest(string solution, bool allowWorkspaceDiagnostics)
    {
        return Request(solution, allowWorkspaceDiagnostics) with { AcceptReductions = true };
    }

    // The --add companions name a violation the ViolatedSpec really does report, so a run that got past the
    // gate would succeed rather than refuse for an unrelated reason.
    private static BaselineRequest AddRequest(string solution, bool allowWorkspaceDiagnostics)
    {
        return Request(solution, allowWorkspaceDiagnostics) with
        {
            Add = true,
            Rule = "data-access/no-inline-sql",
            Because = "INC-1234",
            Source = "MyApp.Web.HomeController",
            Target = "System.Data.DataTable"
        };
    }

    private static BaselineRequest Request(string solution, bool allowWorkspaceDiagnostics)
    {
        return new BaselineRequest(
            solution, CliRunner.ViolatedSpecDll, false, false, false, null, null, null, null, null,
            Path.GetDirectoryName(Path.GetFullPath(solution))!, allowWorkspaceDiagnostics);
    }

    private static Task<CliResult> RunBaselineAsync(
        TempFixtureWorkspace workspace,
        IReadOnlyList<string> diagnostics,
        Func<string, bool, BaselineRequest> request,
        bool allowWorkspaceDiagnostics,
        IReadOnlyList<string>? failedProjects = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        var source = new DiagnosticInjectingSolutionSource(
            diagnostics, failedProjects, restoreFailedProjects: restoreFailedProjects);

        return CliResult.CapturedAsync((output, error) => new BaselineRunner(output, error, source)
            .RunAsync(request(workspace.SolutionPath, allowWorkspaceDiagnostics), Ct));
    }

    private static Task<CliResult> RunCheckAsync(
        TempFixtureWorkspace workspace, IReadOnlyList<string> diagnostics, bool allowWorkspaceDiagnostics,
        IReadOnlyList<string>? failedProjects = null)
    {
        var source = new DiagnosticInjectingSolutionSource(diagnostics, failedProjects);
        var request = new CheckRequest(
            workspace.SolutionPath, CliRunner.ViolatedSpecDll, false, null,
            SolutionPaths.SolutionDirectoryOf(workspace.SolutionPath), true, null,
            allowWorkspaceDiagnostics, null, null, DocumentGrain.Full);

        return CliResult.CapturedAsync((output, error) => new CheckRunner(output, error, source, new FakeEnvironment()).RunAsync(request, Ct));
    }
}

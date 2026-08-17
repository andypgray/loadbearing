using System.Text.Json;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Tests.Mcp;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The one answer to a partially-loaded workspace, against a solution that really does load partially.
/// </summary>
/// <remarks>
///     <para>
///         Every other fixture in this suite is restored before it is checked, which is exactly why nothing
///         in here caught the field failures: <c>arch_graph</c> crashing with an invariant violation on an
///         unbuilt tree, and <c>check</c>/<c>baseline</c>/<c>graph</c>/<c>status</c> giving four different
///         answers to one condition. The BrokenApp fixture supplies the missing state — its solution names
///         a project the tree does not contain, so the load reports real failures and the project that does
///         load carries Roslyn error symbols where its reference to the absent one should be. See
///         <c>BrokenApp.Web.csproj</c>'s header for why "just leave it unrestored" was measured and does
///         not reproduce <em>that</em>, and for the siting constraints that keep the fixture broken.
///     </para>
///     <para>
///         <b>It is also the only bed where both causes compose.</b> The tree is never restored — the fixture
///         forbids an <c>obj/</c>, and <c>BrokenApp.Web</c> could not be restored in isolation anyway, since it
///         references the deliberately-absent project — so its two SDK-style projects carry the restore cause
///         while the third carries the load cause. Every refusal below therefore writes two blocks, and the
///         order they come in is asserted rather than assumed: nothing else in the suite reaches the
///         composition over a real tree, because the cheaper beds inject one list or the other.
///     </para>
///     <para>
///         Cold invocations throughout (<see cref="CliRunner.InvokeColdAsync(string[])" />), never the warm pool: a
///         pooled session keyed on a shared workspace must never be handed a broken one. The MCP leg gets
///         its own harness, which composes its own session.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class PartialLoadWorkspaceE2ETests
{
    // The lede of each verb's refusal. The evidence — one indented line per failed project — and the shared
    // tail follow it, and BrokenApp.Contracts.csproj is asserted separately wherever the evidence is the
    // subject: a refusal that could not name what failed is the defect these four exist to prevent.
    private const string CheckGateLine =
        "error: the model is incomplete — 1 project failed to load, so check cannot pass:";

    private const string StatusGateLine =
        "error: the model is incomplete — 1 project failed to load, so status cannot report the burndown: "
        + "unloaded projects contribute no violations, so every count reads low:";

    private const string BaselineGateLine =
        "error: the model is incomplete — 1 project failed to load, so no baseline was written: a baseline "
        + "captured from a partial model signs off debt that was never measured:";

    private const string RenderGateLine =
        "error: the model is incomplete — 1 project failed to load, so nothing was rendered: a card whose "
        + "project failed to load cannot be placed and would be dropped from the committed files, and "
        + "--diagram would draw a survey missing whole projects:";

    // The restore lede each of the four verbs writes below its load block. Only the count and the noun phrase
    // are shared; what each says is at stake is not, which is why they are separate strings in production too.
    private const string CheckRestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 2 projects, so check cannot pass: "
        + "package references that resolved to nothing produce no edges, so a rule about a package is measured "
        + "against a model that never saw it:";

    private const string StatusRestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 2 projects, so status cannot "
        + "report the burndown: a rule about a package the model never saw contributes no violations, so every "
        + "count reads low:";

    private const string BaselineRestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 2 projects, so no baseline was "
        + "written: every violation resting on a package edge is absent from this model, so the baseline would "
        + "sign off debt it could not see:";

    private const string RenderRestoreGateLine =
        "error: the model is incomplete — NuGet packages did not resolve for 2 projects, so nothing was "
        + "rendered: a card would be committed describing dependencies the model never resolved, and --diagram "
        + "would draw a survey with the external edges missing:";

    // The project BrokenApp declares and the tree does not contain — the whole reason this fixture exists,
    // and now the thing every refusal in this class names.
    private const string MissingProject = "BrokenApp.Contracts.csproj";

    // The message shape of the crash this whole change exists to remove. Asserted absent, never present:
    // a refusal that names a symbol the caller never wrote is the failure, not the fix.
    private const string InvariantViolationFragment = "has no C# declaration meaning";

    [Fact]
    public async Task Graph_PartiallyLoadedWorkspace_RefusesNamingTheFailuresRatherThanCrashing()
    {
        // graph is the entry point that needs no spec, so it is the first command a stranger runs on an
        // unfamiliar codebase — and the one that used to answer with a stack trace naming a symbol from a
        // project that did not load.
        using TempFixtureWorkspace workspace = BrokenApp();

        CliResult graph = await CliRunner.InvokeColdAsync("graph", workspace.SolutionPath, "--no-cache");

        graph.ShouldRefuseWith();
        graph.Err.ShouldContain("the model is incomplete");
        graph.Err.ShouldContain("graph cannot survey the codebase");
        // It names what failed, inline — the MCP surface discards the error writer, so a refusal that
        // pointed at "the warnings above" would name evidence half its callers cannot reach.
        graph.Err.ShouldContain(MissingProject);
        // ... says which MSBuild opened them, which is nearly always the next question ...
        graph.Err.ShouldContain("MSBuild for this run:");
        // ... and says what to do about it, in both dialects.
        graph.Err.ShouldContain("Restore and build the solution first");
        graph.Err.ShouldContain("--allow-workspace-diagnostics");
        graph.Err.ShouldContain("allowWorkspaceDiagnostics");
        // ... and the second cause, which on a survey is the sharper of the two: these projects are all
        // present with all their types, so a reader has nothing to notice as absent.
        graph.ShouldTellBothCausesLoadFirst(
            "the model is incomplete — 1 project failed to load, so graph cannot survey the codebase:",
            "the model is incomplete — NuGet packages did not resolve for 2 projects, so graph cannot survey "
            + "the codebase: the external references a survey exists to show are exactly what did not resolve:");

        graph.Err.ShouldNotContain(InvariantViolationFragment);
        graph.Err.ShouldNotContain("NotApplicable");
        graph.Err.ShouldNotContain("Unhandled exception");
        graph.Out.ShouldBeEmpty(); // it refused before there was a document to write
    }

    [Fact]
    public async Task Graph_PartiallyLoadedWorkspaceWithFlag_SurveysAndCarriesTheVerdictInTheDocument()
    {
        // The opt-out reaches extraction, which is the path that used to throw: the survey is produced from
        // a compilation whose unresolved symbols now mint ordinary externals.
        using TempFixtureWorkspace workspace = BrokenApp();

        CliResult human = await CliRunner.InvokeColdAsync(
            "graph", workspace.SolutionPath, "--no-cache", "--allow-workspace-diagnostics");

        human.ShouldSucceed();
        human.Out.ShouldContain("Codebase survey: BrokenApp.sln");
        human.Out.ShouldContain("BrokenApp.Core");
        human.Err.ShouldContain("warning: Project file not found:"); // the diagnostics still render
        human.Err.ShouldNotContain(InvariantViolationFragment);

        CliResult json = await CliRunner.InvokeColdAsync(
            "graph", workspace.SolutionPath, "--no-cache", "--json", "--allow-workspace-diagnostics");

        json.ShouldSucceed();
        using JsonDocument _ = json.ShouldHaveJsonStdout();
        // The survey is a partial map, and says so in the document — the only channel an MCP client has.
        json.Out.ShouldContain("\"workspaceDiagnostics\"");
        json.Out.ShouldContain(MissingProject);
        json.Out.ShouldContain("\"failedProjects\"");
        json.Out.ShouldContain("\"modelIncomplete\": true");
    }

    [Fact]
    public async Task CheckAndStatus_PartiallyLoadedWorkspace_FailClosedAndOptBackInTogether()
    {
        // The two verbs that render before they gate, on one workspace. check's own refusal was unreachable
        // on a tree like this until extraction stopped throwing: it ran extraction before computing its gate,
        // so the crash beat the refusal.
        using TempFixtureWorkspace workspace = BrokenApp();

        CliResult check = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");
        check.ShouldRefuseWith(CheckGateLine, MissingProject);
        check.ShouldTellBothCausesLoadFirst(CheckGateLine, CheckRestoreGateLine);
        check.Err.ShouldNotContain(InvariantViolationFragment);

        CliResult checkAllowed = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache",
            "--allow-workspace-diagnostics");
        checkAllowed.Exit.ShouldNotBe(2, checkAllowed.Err); // 0 or 1 — a verdict, not a refusal
        checkAllowed.Err.ShouldNotContain("error: the model is incomplete");

        CliResult status = await CliRunner.InvokeColdAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");
        status.ShouldRefuseWith(StatusGateLine, MissingProject);
        status.ShouldTellBothCausesLoadFirst(StatusGateLine, StatusRestoreGateLine);
        status.Out.ShouldNotBeEmpty(); // status renders the burndown it does have, then gates

        CliResult statusAllowed = await CliRunner.InvokeColdAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json",
            "--allow-workspace-diagnostics");
        statusAllowed.ShouldSucceed();
        statusAllowed.Out.ShouldContain("\"workspaceDiagnostics\"");
        statusAllowed.Out.ShouldContain("\"modelIncomplete\": true");
        statusAllowed.Out.ShouldContain(MissingProject); // and the document names it, not only the exit code
    }

    [Fact]
    public async Task Check_PartiallyLoadedWorkspaceWithNoSpec_BlamesTheLoadRatherThanTheMissingSpec()
    {
        // Spec resolution runs before the incomplete-model gate can fire, so on a tree like this the reader
        // met the convention's own failure first: "no solution project references Zphil.LoadBearing.dll —
        // pass --spec to name one". Both halves of that were misdirection. The project that would have
        // matched may be one of the ones that failed, and no --spec argument repairs a load. So the refusal
        // names them.
        using TempFixtureWorkspace workspace = BrokenApp();

        CliResult check = await CliRunner.InvokeColdAsync("check", workspace.SolutionPath, "--no-cache");

        check.ShouldRefuseWith("one or more projects failed to load");
        check.Err.ShouldContain(MissingProject); // the evidence, inline
        check.Err.ShouldContain("Restore and build the solution first");
        check.Err.ShouldNotContain("Pass --spec to name one");
        check.Err.ShouldNotContain(InvariantViolationFragment);
    }

    [Fact]
    public async Task Baseline_PartiallyLoadedWorkspace_RefusesWithoutWritingAnything()
    {
        // The stakes command. Nothing under the solution root may appear as a result of this run.
        using TempFixtureWorkspace workspace = BrokenApp();
        string[] before = FilesUnder(workspace);

        CliResult baseline = await CliRunner.InvokeColdAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--init");

        baseline.ShouldRefuseWith(BaselineGateLine, MissingProject);
        baseline.ShouldTellBothCausesLoadFirst(BaselineGateLine, BaselineRestoreGateLine);
        baseline.Err.ShouldNotContain(InvariantViolationFragment);
        FilesUnder(workspace)
            .ShouldBe(before);
    }

    [Fact]
    public async Task Render_PartiallyLoadedWorkspace_RefusesWithoutWritingAnything()
    {
        // The other stakes command. What render writes is committed context, and from a partial model it
        // composes wrong rather than short: a card whose project failed to load resolves no directory and is
        // dropped. So nothing under the solution root may appear, or change, as a result of this run.
        using TempFixtureWorkspace workspace = BrokenApp();
        string[] before = FilesUnder(workspace);

        CliResult render = await CliRunner.InvokeColdAsync(
            "render", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll);

        render.ShouldRefuseWith(RenderGateLine, MissingProject);
        render.ShouldTellBothCausesLoadFirst(RenderGateLine, RenderRestoreGateLine);
        render.Err.ShouldNotContain(InvariantViolationFragment);
        render.Out.ShouldBeEmpty(); // it refused before the first wrote/unchanged line
        FilesUnder(workspace)
            .ShouldBe(before);
    }

    [Fact]
    public async Task Render_PartiallyLoadedWorkspaceWithFlag_WritesTheBlockAndStillWarns()
    {
        // The opt-out, and the negative control for "wrote nothing" above: the operator takes the partial
        // model, the load failures still print, and the root block lands. The tree written to is this
        // class's own copy, which the next test's arrange resets.
        using TempFixtureWorkspace workspace = BrokenApp();
        string rootAgents = Path.Combine(Path.GetDirectoryName(workspace.SolutionPath)!, "AGENTS.md");

        CliResult render = await CliRunner.InvokeColdAsync(
            "render", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--allow-workspace-diagnostics");

        render.ShouldSucceed("wrote AGENTS.md");
        render.Err.ShouldContain("warning: Project file not found:"); // the diagnostics still render
        render.Err.ShouldNotContain("error: the model is incomplete"); // but the gate did not fire
        File.Exists(rootAgents)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Context_PartiallyLoadedWorkspace_OpensWithACaveatAndStillAnswers()
    {
        // The verb that never gates: context is a lookup an agent runs mid-edit, where a partial answer beats
        // none. But "no architecture scope covers this path" is exactly the answer a partial load turns into
        // a false all-clear, so the failures ride the body — stdout is the only channel this verb has (no CLI
        // twin, no --json). Driven through the runner directly, because there is no CLI verb to invoke.
        using TempFixtureWorkspace workspace = BrokenApp();
        string solutionDirectory = Path.GetDirectoryName(workspace.SolutionPath)!;
        var output = new StringWriter();

        int exit = await new ContextRunner(output).RunAsync(
            new ContextRequest(
                Path.Combine("BrokenApp.Web", "WidgetController.cs"), workspace.SolutionPath,
                CliRunner.CleanSpecDll, solutionDirectory),
            Ct);

        exit.ShouldBe(0); // a lookup, never a gate
        var answer = output.ToString();
        answer.ShouldStartWith("caveat: the model is incomplete"); // the caveat opens the body, above the answer
        answer.ShouldContain(MissingProject); // naming what failed, inline: there is no stderr here
        answer.ShouldContain("MSBuild for this run:");
        answer.ShouldContain("Restore and build the solution first (dotnet build), then retry for a whole answer.");
        // Both causes ride the body, load first, because the caveat is the whole of what this verb can say —
        // and the path being looked up is under one of the projects the second cause names.
        answer.ShouldContain(
            "caveat: the model is incomplete — NuGet packages did not resolve for 2 projects, so a rule about "
            + "a package cannot be trusted to have been measured for paths under them:");
        answer.ShouldContain("Restore the solution first (dotnet restore), then retry for a whole answer.");
        answer.ShouldNotContain(InvariantViolationFragment);

        // ... and the answer the partial model still supports follows, one blank line below the caveat.
        string[] lines = answer.NormalizedLines()
            .TrimEnd()
            .Split('\n');
        int pointer = Array.FindIndex(lines, line => line.StartsWith("No architecture scope covers", StringComparison.Ordinal));
        pointer.ShouldBeGreaterThan(0, answer);
        lines[pointer - 1]
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchGraph_PartiallyLoadedWorkspace_IsAnErrorResultThenSurveysWithTheOptOut()
    {
        // The MCP twin, on the surface where the field crash was observed. A tool error rather than an
        // exception dump is the whole point: the agent has to be able to read the refusal as actionable.
        using TempFixtureWorkspace workspace = BrokenApp();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            new McpServerBinding(workspace.SolutionPath, null, Path.GetDirectoryName(workspace.SolutionPath)!), Ct);

        CallToolResult refused = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);

        refused.IsError.ShouldBe(true);
        string refusal = refused.ShouldHaveTextContent();
        refusal.ShouldContain("the model is incomplete");
        refusal.ShouldContain(MissingProject); // the evidence, inline: there is no stderr here
        refusal.ShouldContain("Restore and build the solution first");
        refusal.ShouldContain("allowWorkspaceDiagnostics");
        // The second cause reaches the agent too, and inline for the same reason as the first: this surface
        // has no stderr, so a block that pointed at warnings above would name nothing reachable.
        refusal.ShouldContain("NuGet packages did not resolve for 2 projects");
        foreach (string project in PartialLoadRefusalAssertions.UnrestoredProjects) refusal.ShouldContain(project);
        refusal.ShouldNotContain(InvariantViolationFragment);
        // A UserErrorException is expected input, not a bug, so the server logs nothing about it.
        harness.Logs.Warnings.ShouldBeEmpty();

        CallToolResult surveyed = await harness.Client.CallToolAsync(
            "arch_graph", new Dictionary<string, object?> { ["allowWorkspaceDiagnostics"] = true }, cancellationToken: Ct);

        surveyed.IsError.ShouldNotBe(true);
        string survey = surveyed.ShouldHaveTextContent();
        survey.ShouldContain("BrokenApp.Core");
        survey.ShouldContain("\"modelIncomplete\": true");
        survey.ShouldContain("\"workspaceDiagnostics\"");
    }

    /// <summary>
    ///     A private copy of the BrokenApp fixture, with the arrange-time guard that keeps this whole class
    ///     honest: <c>BrokenApp.Contracts</c> must still be absent. Creating that directory — or committing a
    ///     stub csproj for it — is the single edit that would silently turn every fact above into a test of
    ///     nothing, so it fails loudly here instead.
    /// </summary>
    private static TempFixtureWorkspace BrokenApp()
    {
        var workspace = new TempFixtureWorkspace("PartialLoadSolutions/BrokenApp", "BrokenApp.sln", false);
        string missingProject = Path.Combine(
            Path.GetDirectoryName(workspace.SolutionPath)!, "BrokenApp.Contracts", "BrokenApp.Contracts.csproj");

        File.Exists(missingProject)
            .ShouldBeFalse(
                customMessage:
                $"The BrokenApp fixture healed: '{missingProject}' exists, so the solution now loads completely and "
                + "every fact in PartialLoadWorkspaceE2ETests would pass without exercising the incomplete-model gate.");

        return workspace;
    }

    // Every file under the solution root, ordered, excluding build output — the "wrote nothing" oracle.
    private static string[] FilesUnder(TempFixtureWorkspace workspace)
    {
        string root = Path.GetDirectoryName(workspace.SolutionPath)!;
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>
///     The composed-refusal claim this suite makes about a <see cref="CliResult" />, as an extension so the
///     run under test reads as the subject beside the exit-contract assertions it sits with.
/// </summary>
/// <remarks>
///     <para>
///         <c>file</c>-scoped rather than added to <see cref="CliResultAssertions" />: it carries this
///         fixture's own project list, which is evidence about BrokenApp rather than vocabulary about the CLI.
///         It graduates to the shared file the day a second suite wants it.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="Zphil.LoadBearing.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
file static class PartialLoadRefusalAssertions
{
    /// <summary>
    ///     The two projects that do load, out of a tree that was never restored. They carry the second cause,
    ///     so this fixture is also the only bed where both blocks are composed on a real tree — which is what
    ///     the ordering assertion below is for.
    /// </summary>
    internal static readonly string[] UnrestoredProjects = ["BrokenApp.Core.csproj", "BrokenApp.Web.csproj"];

    /// <summary>
    ///     Asserts one refusal carries <em>both</em> causes, each naming its own projects, with the load block
    ///     above the restore block.
    /// </summary>
    /// <remarks>
    ///     The ordering is a rule, not an accident: a project that never loaded is more fundamentally broken
    ///     than one that loaded without its packages, and the two ask for different repairs. Nothing else in
    ///     the suite composes both blocks over a real tree — the cheaper beds inject one list or the other —
    ///     so this is where the composition is measured rather than assembled.
    /// </remarks>
    internal static void ShouldTellBothCausesLoadFirst(
        this CliResult result, string loadLede, string restoreLede)
    {
        string channel = result.Err;
        string report = Describe(result);

        channel.ShouldContain(loadLede, report);
        channel.ShouldContain(restoreLede, report);
        foreach (string project in UnrestoredProjects) channel.ShouldContain(project, report);

        channel.IndexOf(loadLede, StringComparison.Ordinal)
            .ShouldBeLessThan(
                channel.IndexOf(restoreLede, StringComparison.Ordinal),
                $"The restore block should follow the load block.{Environment.NewLine}{report}");
    }

    /// <summary>
    ///     The exit code and stderr — the channel this assertion reads, and the whole of what the refusal said.
    /// </summary>
    private static string Describe(CliResult result)
    {
        return $"The CLI exited {result.Exit}.{Environment.NewLine}"
               + $"stderr:{Environment.NewLine}{result.Err.TrimEnd()}";
    }
}

using System.Text.Json;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Mcp;
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
///         not reproduce this, and for the siting constraints that keep the fixture broken.
///     </para>
///     <para>
///         Cold invocations throughout (<see cref="CliRunner.InvokeColdAsync" />), never the warm pool: a
///         pooled session keyed on a shared workspace must never be handed a broken one. The MCP leg gets
///         its own harness, which composes its own session.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class PartialLoadWorkspaceE2ETests
{
    private const string CheckGateLine =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so check "
        + "cannot pass. Pass --allow-workspace-diagnostics to check against the partial model anyway.";

    private const string StatusGateLine =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so status "
        + "cannot report the burndown: unloaded projects contribute no violations, so every count reads low. "
        + "Pass --allow-workspace-diagnostics to report against the partial model anyway.";

    private const string BaselineGateLine =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so no "
        + "baseline was written: a baseline captured from a partial model signs off debt that was never measured. "
        + "Pass --allow-workspace-diagnostics to baseline against the partial model anyway.";

    private const string RenderGateLine =
        "error: the model is incomplete — one or more projects failed to load (see the warnings above), so nothing "
        + "was rendered: a card whose project failed to load cannot be placed and would be dropped from the "
        + "committed files, and --diagram would draw a survey missing whole projects. "
        + "Pass --allow-workspace-diagnostics to render from the partial model anyway.";

    // The message shape of the crash this whole change exists to remove. Asserted absent, never present:
    // a refusal that names a symbol the caller never wrote is the failure, not the fix.
    private const string InvariantViolationFragment = "has no C# declaration meaning";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        graph.Err.ShouldContain("BrokenApp.Contracts.csproj");
        // ... says which MSBuild opened them, which is nearly always the next question ...
        graph.Err.ShouldContain("MSBuild for this run:");
        // ... and says what to do about it, in both dialects.
        graph.Err.ShouldContain("Restore and build the solution first");
        graph.Err.ShouldContain("--allow-workspace-diagnostics");
        graph.Err.ShouldContain("allowWorkspaceDiagnostics");

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
        json.Out.ShouldContain("BrokenApp.Contracts.csproj");
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
        check.ShouldRefuseWith();
        check.Err.ShouldContain(CheckGateLine);
        check.Err.ShouldNotContain(InvariantViolationFragment);

        CliResult checkAllowed = await CliRunner.InvokeColdAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache",
            "--allow-workspace-diagnostics");
        checkAllowed.Exit.ShouldNotBe(2, checkAllowed.Err); // 0 or 1 — a verdict, not a refusal
        checkAllowed.Err.ShouldNotContain("error: the model is incomplete");

        CliResult status = await CliRunner.InvokeColdAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache");
        status.ShouldRefuseWith();
        status.Err.ShouldContain(StatusGateLine);
        status.Out.ShouldNotBeEmpty(); // status renders the burndown it does have, then gates

        CliResult statusAllowed = await CliRunner.InvokeColdAsync(
            "status", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json",
            "--allow-workspace-diagnostics");
        statusAllowed.ShouldSucceed();
        statusAllowed.Out.ShouldContain("\"workspaceDiagnostics\"");
        statusAllowed.Out.ShouldContain("\"modelIncomplete\": true");
    }

    [Fact]
    public async Task Baseline_PartiallyLoadedWorkspace_RefusesWithoutWritingAnything()
    {
        // The stakes command. Nothing under the solution root may appear as a result of this run.
        using TempFixtureWorkspace workspace = BrokenApp();
        string[] before = FilesUnder(workspace);

        CliResult baseline = await CliRunner.InvokeColdAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.CleanSpecDll, "--init");

        baseline.ShouldRefuseWith();
        baseline.Err.ShouldContain(BaselineGateLine);
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

        render.ShouldRefuseWith();
        render.Err.ShouldContain(RenderGateLine);
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
        answer.ShouldContain("BrokenApp.Contracts.csproj"); // naming what failed, inline: there is no stderr here
        answer.ShouldContain("MSBuild for this run:");
        answer.ShouldContain("Restore and build the solution first (dotnet build), then retry for a whole answer.");
        answer.ShouldNotContain(InvariantViolationFragment);

        // ... and the answer the partial model still supports follows, one blank line below the caveat.
        string[] lines = answer.Replace("\r\n", "\n")
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
        refusal.ShouldContain("BrokenApp.Contracts.csproj"); // the evidence, inline: there is no stderr here
        refusal.ShouldContain("Restore and build the solution first");
        refusal.ShouldContain("allowWorkspaceDiagnostics");
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

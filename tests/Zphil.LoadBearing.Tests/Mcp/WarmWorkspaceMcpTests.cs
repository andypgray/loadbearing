using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Mcp;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The Phase 11 D1 acceptance: with the warm <see cref="WorkspaceSession" /> wired behind the MCP
///     tools, a server answers many tool calls against one loaded solution, reconciled against disk at each
///     call. These tests drive the real <see cref="McpPipelineHarness" /> (the production DI graph, warm by
///     default) and assert on the deterministic observables — the session's <c>SweepContentReads</c> /
///     <c>FullReloadCount</c> counters reached through the harness <see cref="McpPipelineHarness.Services" />
///     accessor, byte-equality against a fresh cold CLI run, and cold/warm source selection — never on wall
///     time. Serialized with the other workspace-loading suites: each case opens a real
///     <c>MSBuildWorkspace</c>. The existing <see cref="CliMcpParityTests" /> is the broad warm-path parity
///     net (it now runs warm by default); this suite pins the warm-specific behaviour that parity cannot see.
/// </summary>
[Collection("Serial")]
public sealed class WarmWorkspaceMcpTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchCheck_SourceFileEditedOnDisk_ReflectsEditAndMatchesColdCli()
    {
        // Arrange — a warm server bound to a private fixture copy + the frozen spec, whose containment turns
        // a NEW inbound reference into the frozen scope hard-red, so an on-disk edit visibly changes the
        // check report.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.FrozenSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = TextOf(await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct));

        // Act — add a new inbound reference (HomeController -> BillingCalculator) on disk, then re-check the
        // still-warm server. A fresh cold CLI run over the same edited tree is the parity oracle.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        EditOnDisk(homeController, InsertNewCalculatorMember);
        string after = TextOf(await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct));
        CliResult coldEdited = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.FrozenSpecDll, "--json");

        // Assert — the warm re-check reflects the edit (the new red edge appears, and the payload changed)
        // and is byte-identical to the cold run on the edited tree.
        after.ShouldContain("MyApp.Legacy.Billing.BillingCalculator");
        Normalize(after).ShouldNotBe(Normalize(before));
        Normalize(after).ShouldBe(Normalize(coldEdited.Out));

        // …and the incremental store re-walked exactly the edited project (HomeController is in Web) plus its
        // reverse-dependent Domain — Billing was reused, not re-extracted.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_SteadyStateNoDiskChange_ReadsAndReloadsNothing()
    {
        // Arrange — load, then promote every document past the racy window (backdate + one reconcile) so the
        // measured steady state is a pure O(stat) no-op (roz's steady-state pattern, driven through the tool).
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.CleanSpecDll), Ct);

        var session = harness.Services.GetRequiredService<WorkspaceSession>();
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();
        // Discover exactly as WarmSolutionSource does, so the direct session calls target the same path the
        // tool loads and never trip the "path changed => reload" branch.
        string solutionPath = ModelPipeline.DiscoverSolution(
            fixture.SolutionPath, Path.GetDirectoryName(fixture.SolutionPath)!);

        await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        WorkspaceSnapshot loaded = await session.GetCurrentAsync(solutionPath, Ct);
        BackdateAllDocuments(loaded);
        await session.GetCurrentAsync(solutionPath, Ct); // warmup: content-verifies + promotes every document
        long readsBefore = session.SweepContentReads;
        long reloadsBefore = session.FullReloadCount;

        // Act — two more arch_check calls with disk untouched.
        string first = TextOf(await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct));
        string second = TextOf(await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct));

        // Assert — the warm reconcile read no content and triggered no reload, the incremental store re-walked
        // nothing on the steady-state call, and the two responses are byte-identical.
        (session.SweepContentReads - readsBefore).ShouldBe(0);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        store.LastReExtractedProjects.ShouldBeEmpty();
        Normalize(second).ShouldBe(Normalize(first));
    }

    [Fact]
    public async Task ArchCheck_CsprojTouchedOnDisk_NextCallTripsExactlyOneReload()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.CleanSpecDll), Ct);

        var session = harness.Services.GetRequiredService<WorkspaceSession>();
        await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct); // warm load
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch: bump a project file's mtime, then re-check.
        File.SetLastWriteTimeUtc(
            fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj"), DateTime.UtcNow.AddSeconds(2));
        CallToolResult after = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);

        // Assert — exactly one full reload, and the call still succeeded.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.IsError.ShouldNotBe(true);
    }

    [Fact]
    public async Task SolutionSource_WarmWorkspaceDisabled_ResolvesColdSource()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.CleanSpecDll), Ct);

        // Act — flip the disable flag on the fake environment, then resolve the composed source. The factory
        // reads the flag lazily on first resolve, so setting it before this resolve selects the cold path.
        harness.Environment.SetVariable(McpServerCommand.DisableWarmWorkspaceVariable, "true");
        var source = harness.Services.GetRequiredService<ISolutionSource>();

        // Assert
        source.ShouldBeOfType<ColdSolutionSource>();
    }

    [Fact]
    public async Task TwoConcurrentToolCalls_BothSucceed()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll), Ct);

        // Act — fire two tool calls at once. The MCP SDK dispatches them in parallel; the session gate
        // serializes the concurrent first-load, and both callers share the one immutable snapshot.
        var first = harness.Client.CallToolAsync("arch_check", cancellationToken: Ct).AsTask();
        var second = harness.Client.CallToolAsync("arch_status", cancellationToken: Ct).AsTask();
        var results = await Task.WhenAll(first, second);

        // Assert — both succeeded with content.
        results.ShouldAllBe(result => result.IsError != true);
        results.Select(TextOf).ShouldAllBe(text => text.Length > 0);
    }

    [Fact]
    public async Task ArchCheck_SpecDllDeletedMidSession_ReturnsSameErrorAsColdCli()
    {
        // Arrange — bind to a throwaway copy of a spec DLL so deleting it cannot disturb the shared fixture.
        string tempDirectory = Path.Combine(
            Path.GetTempPath(), "loadbearing-warm-mcp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tempSpec = Path.Combine(tempDirectory, "CopiedSpec.dll");
        File.Copy(CliRunner.CleanSpecDll, tempSpec);
        try
        {
            await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
                Binding(CliRunner.MyAppSolution, tempSpec), Ct);

            // Warm the session WITHOUT loading the spec: arch_graph opens the workspace (it is spec-free), so
            // the spec DLL stays unlocked and can be deleted to model a mid-session removal.
            CallToolResult graph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
            graph.IsError.ShouldNotBe(true);

            // Act — delete the bound spec DLL, then check against the still-warm workspace.
            File.Delete(tempSpec);
            CallToolResult check = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
            CliResult coldCheck = await CliRunner.InvokeAsync(
                "check", CliRunner.MyAppSolution, "--spec", tempSpec, "--json");
            coldCheck.Exit.ShouldBe(2);

            // Assert — the tool errors with exactly the text the cold CLI wrote to stderr in the same state.
            check.IsError.ShouldBe(true);
            Normalize(TextOf(check)).ShouldBe(Normalize(coldCheck.Err));
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static McpServerBinding Binding(string? solution, string? spec)
    {
        string workingDirectory = solution is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(solution))!;
        return new McpServerBinding(solution, spec, workingDirectory);
    }

    // Inserts a member referencing the frozen scope's interior just before the class's final closing brace —
    // the FreezeContainmentE2ETests edit, so the resulting red edge is the same one that suite pins.
    private static string InsertNewCalculatorMember(string source)
    {
        int lastBrace = source.LastIndexOf('}');
        return source[..lastBrace] + "    public BillingCalculator NewCalculator() => new BillingCalculator();\n}\n";
    }

    private static void EditOnDisk(string path, Func<string, string> transform)
    {
        string content = File.ReadAllText(path);
        File.WriteAllText(path, transform(content));
        // A future mtime guarantees the reconcile sweep sees a delta against the load-time fingerprint.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
    }

    private static void BackdateAllDocuments(WorkspaceSnapshot snapshot)
    {
        DateTime wellPast = DateTime.UtcNow.AddDays(-1);
        var paths = snapshot.Solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .OfType<string>()
            .Distinct();

        foreach (string path in paths)
            try
            {
                File.SetLastWriteTimeUtc(path, wellPast);
            }
            catch (IOException)
            {
                // Best-effort: a file we cannot re-stamp stays racy and simply re-reads, which the warmup
                // baseline captured before the measured window.
            }
    }

    private static string TextOf(CallToolResult result)
    {
        return ((TextContentBlock)result.Content.Single()).Text;
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}
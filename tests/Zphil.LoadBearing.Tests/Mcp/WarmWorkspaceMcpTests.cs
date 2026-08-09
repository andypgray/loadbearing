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
///     With the warm <see cref="WorkspaceSession" /> wired behind the MCP
///     tools, a server answers many tool calls against one loaded solution, reconciled against disk at each
///     call. These tests drive the real <see cref="McpPipelineHarness" /> (the production DI graph, warm by
///     default) and assert on the deterministic observables — the session's <c>SweepContentReads</c> /
///     <c>FullReloadCount</c> counters reached through the harness <see cref="McpPipelineHarness.Services" />
///     accessor, byte-equality against a fresh cold CLI run, and cold/warm source selection — never on wall
///     time. Serialized with the other workspace-loading suites: each case opens a real
///     <c>MSBuildWorkspace</c>. The existing <see cref="CliMcpParityTests" /> is the broad warm-path parity
///     net (it now runs warm by default); this suite pins the warm-specific behaviour that parity cannot see.
/// </summary>
/// <remarks>
///     Every CLI leg here goes through <see cref="CliRunner.InvokeColdAsync" />, never the warm-by-default
///     <see cref="CliRunner.InvokeAsync" />. The oracle in each case is a <em>freshly loaded</em> run over the
///     edited tree: warm-equals-cold is the claim, so the reference side has to be genuinely cold or the
///     comparison proves nothing.
/// </remarks>
[Collection("Serial")]
public sealed class WarmWorkspaceMcpTests
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";

    private const string SaveMemberId = "M:MyApp.Web.HomeController.Save";
    private const string LoadMemberId = "M:MyApp.Web.HomeController.Load";
    private const string DeleteMemberId = "M:MyApp.Web.HomeController.Delete";
    private const string SaveAsyncMemberId = "M:MyApp.Web.HomeController.SaveAsync";

    // A one-rule spec (member-subject MustAcceptParameter): Web Task-returning methods must accept a
    // CancellationToken. Compiled to a throwaway DLL by SpecAssemblyCompiler because no committed fixture
    // spec carries this rule in isolation (MyAppViolatedSpec has it, plus fourteen rules of noise).
    // String-selected subject + BCL anchors only, so the spec depends on nothing but the core and the BCL.
    private const string AcceptCancellationSpecSource = """
                                                        using System.Threading;
                                                        using System.Threading.Tasks;
                                                        using Zphil.LoadBearing;

                                                        namespace WarmAcceptCancellation
                                                        {
                                                            public sealed class AcceptCancellationSpec : IArchitectureSpec
                                                            {
                                                                public void Define(Arch arch)
                                                                {
                                                                    arch.Rule("async/accept-cancellation")
                                                                        .Enforce(arch.Namespace("MyApp.Web.*").Methods
                                                                            .Returning(typeof(Task), typeof(Task<>))
                                                                            .MustAcceptParameter(typeof(CancellationToken)))
                                                                        .Because("Async Web methods must honor cancellation.");
                                                                }
                                                            }
                                                        }
                                                        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchCheck_SourceFileEditedOnDisk_ReflectsEditAndMatchesColdCli()
    {
        // Arrange — a warm server bound to a private fixture copy + the quarantined spec, whose containment turns
        // a NEW inbound reference into the quarantined scope hard-red, so an on-disk edit visibly changes the
        // check report.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.QuarantinedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — add a new inbound reference (HomeController -> BillingCalculator) on disk, then re-check the
        // still-warm server. A fresh cold CLI run over the same edited tree is the parity oracle. The inserted
        // member is the QuarantineContainmentE2ETests edit, so the resulting red edge is the one that suite pins.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        FixtureEdits.EditOnDisk(
            homeController,
            source => FixtureEdits.SpliceMemberLine(
                source, "    public BillingCalculator NewCalculator() => new BillingCalculator();"));
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--json");

        // Assert — the warm re-check reflects the edit (the new red edge appears, and the payload changed)
        // and is byte-identical to the cold run on the edited tree.
        after.ShouldContain("MyApp.Legacy.Billing.BillingCalculator");
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (HomeController is in Web) plus its
        // reverse-dependent Domain — Billing was reused, not re-extracted.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_MemberBanReadAddedOnDisk_ReflectsNewSiteAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose member-level Migrate rule time/inject-clock
        // (uncaptured) is already red on HomeController's two ambient-clock reads (GRAMMAR §4.5).
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.ViolatedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — append a THIRD banned read (another DateTime.Now use) on disk, then re-check the still-warm
        // server. A fresh cold CLI run over the same edited tree is the parity oracle. The new read folds into
        // the existing Now violation's site set rather than minting a new violation identity.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        FixtureEdits.EditOnDisk(
            homeController,
            source => FixtureEdits.SpliceMemberLine(
                source, "    public System.DateTime ExportStampAgain() => System.DateTime.Now;"));
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert — the warm re-check reflects the new member-use site (the Now violation's site set grows from one
        // to two), the payload changed, and it is byte-identical to the cold run on the edited tree.
        NowSiteCount(before)
            .ShouldBe(1);
        NowSiteCount(after)
            .ShouldBe(2);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_UnsuffixedTaskMethodAddedOnDisk_ReflectsNewMemberShapeRedAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose member-subject Migrate rule
        // naming/async-suffix (uncaptured) is already red on HomeController's two unsuffixed Task-returning
        // methods Save/Load (GRAMMAR §4.6). This is the member-SUBJECT analog of the member-USE test above.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.ViolatedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — append a THIRD unsuffixed Task-returning method (Delete) on disk, then re-check the still-warm
        // server. A fresh cold CLI run over the same edited tree is the parity oracle. The new method mints its
        // own member-subject violation identity (M:...Delete) under naming/async-suffix (GRAMMAR §4.6).
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        FixtureEdits.EditOnDisk(
            homeController,
            source => FixtureEdits.SpliceMemberLine(
                source,
                "    public System.Threading.Tasks.Task Delete() => System.Threading.Tasks.Task.CompletedTask;"));
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert — the warm re-check reflects the new member-shape red (the async-suffix subject set grows from
        // {Save, Load} to {Save, Load, Delete}, the new one keying its own M: DocId), the payload changed, and
        // it is byte-identical to the cold run on the edited tree.
        SubjectMembersOf(before, "naming/async-suffix")
            .ShouldBe([SaveMemberId, LoadMemberId], true);
        SubjectMembersOf(after, "naming/async-suffix")
            .ShouldBe([SaveMemberId, LoadMemberId, DeleteMemberId], true);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_CancellationTokenParamAddedOnDisk_ClearsMemberShapeRedAndMatchesColdCli()
    {
        // Arrange — compile a one-rule spec DLL (member-subject MustAcceptParameter) to a private temp dir,
        // then bind a warm server to a fixture copy + that spec. HomeController's three tokenless Task-returning
        // methods Save/Load/SaveAsync are all red (GRAMMAR §4.6). This is the parameter-facts warm analog of the
        // async-suffix member-subject test above — it proves Parameters survive the warm reconcile + cache path.
        // The temp root's delete is best-effort, which is what this DLL needs: a collectible spec ALC may not
        // have released the emitted file's handle by teardown (unload is GC-timed), and the run root's sweep
        // reclaims whatever a failed delete leaves.
        using TempDirectory specTemp = TestTempRoot.Fresh("warm-mcp-param");
        string specDll = specTemp.PathOf("WarmAcceptCancellationSpec.dll");
        SpecAssemblyCompiler.EmitSpecDll(
            AcceptCancellationSpecSource, specDll, "Zphil.LoadBearing.WarmAcceptCancellationSpec");

        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, specDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — add a CancellationToken parameter to the tokenless Save on disk, then re-check the
        // still-warm server. A fresh cold CLI run over the same edited tree is the parity oracle.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        FixtureEdits.EditOnDisk(homeController, AddCancellationTokenToSave);
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — Save's member-shape red clears (Save now accepts the token, so the accept-cancellation
        // subject set shrinks from {Save, Load, SaveAsync} to {Load, SaveAsync}), the payload changed, and
        // the warm result is byte-identical to the cold run on the edited tree.
        SubjectMembersOf(before, "async/accept-cancellation")
            .ShouldBe([SaveMemberId, LoadMemberId, SaveAsyncMemberId], true);
        SubjectMembersOf(after, "async/accept-cancellation")
            .ShouldBe([LoadMemberId, SaveAsyncMemberId], true);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (Web) plus its reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_RegistrationLifetimeFlippedOnDisk_ReflectsChangedCaptiveSetAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose injection Migrate rule
        // di/no-captive-dependencies (uncaptured) is already red on the singleton ReportScheduler's two captive
        // edges: a scoped IOrderFeed and a transient IOrderFormatter (GRAMMAR §4.7). This is the DI-axis analog
        // of the member edit tests above — an on-disk REGISTRATION change (not a reference/member edit).
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.ViolatedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — flip IOrderFeed's registration from scoped to singleton on disk, then re-check the still-warm
        // server. A singleton injecting a now-singleton dependency is not captive, so the IOrderFeed edge drops
        // out of the violation set. A fresh cold CLI run over the same edited tree is the parity oracle.
        string serviceWiring = fixture.PathOf(Web, "ServiceWiring.cs");
        FixtureEdits.EditOnDisk(serviceWiring, FlipOrderFeedToSingleton);
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert — the warm re-check reflects the flipped lifetime (the captive set shrinks from
        // {IOrderFeed, IOrderFormatter} to just {IOrderFormatter}), the payload changed, and it is
        // byte-identical to the cold run on the edited tree — the registration pass re-ran for Web.
        CaptiveInjectedTargets(before)
            .ShouldBe(["MyApp.Web.IOrderFeed", "MyApp.Web.IOrderFormatter"], true);
        CaptiveInjectedTargets(after)
            .ShouldBe(["MyApp.Web.IOrderFormatter"], true);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (ServiceWiring is in Web) plus its
        // reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_SecondSwallowingCatchAddedOnDisk_ReflectsNewCatchSiteAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, whose catch Migrate rule exceptions/no-general-catch
        // (uncaptured) is already red on ReportEndpoint's blanket catch (GRAMMAR §4.8). This is the catch-axis
        // analog of the member-use edit test: adding a SECOND catch (Exception) to the SAME type is the SAME
        // (source, caught) identity, so the one catch violation just grows a second site — not a new identity.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.ViolatedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — append a second swallowing catch (System.Exception) on the SAME Web type on disk, then re-check
        // the still-warm server. A fresh cold CLI run over the same edited tree is the parity oracle. The second
        // site folds into the existing (ReportEndpoint, System.Exception) violation (GRAMMAR §4.8).
        string reportEndpoint = fixture.PathOf(Web, "ReportEndpoint.cs");
        FixtureEdits.EditOnDisk(
            reportEndpoint,
            source => FixtureEdits.SpliceMemberLine(
                source,
                "    public int RenderAgain(int id) { try { return id; } catch (System.Exception) { return -1; } }"));
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert — the violation identity is unchanged (still ONE catch violation for the (ReportEndpoint,
        // Exception) pair), its site set grows from one to two, the payload changed, and it is byte-identical
        // to the cold run on the edited tree.
        CatchViolationCount(before)
            .ShouldBe(1);
        CatchViolationCount(after)
            .ShouldBe(1);
        CatchSiteCount(before)
            .ShouldBe(1);
        CatchSiteCount(after)
            .ShouldBe(2);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (ReportEndpoint is in Web) plus its
        // reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_WhenFilterAddedOnDisk_ClearsUnfilteredCatchRedWhileCatchBanStaysRedAndMatchesColdCli()
    {
        // Arrange — a warm server bound to the violated spec, where ReportEndpoint's ONE blanket catch is red
        // under two rules at once: the plain catch ban exceptions/no-general-catch (its site is unfiltered, so
        // both agree today) and the filter-aware exceptions/no-unfiltered-catch. This is the filter-fact analog
        // of the second-catch edit above, and the only place the new fact crosses the dirty-rewalk path. The
        // filter-aware rule also reds ReportPublisher, whose unfiltered catch this edit never touches — the
        // constant that makes the ReportEndpoint drop legible.
        using var fixture = new TempFixtureWorkspace();
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(fixture.SolutionPath, CliRunner.ViolatedSpecDll), Ct);
        var store = harness.Services.GetRequiredService<SessionFragmentStore>();

        string before = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Act — add a `when` filter to that same catch on disk, then re-check the still-warm server. A fresh
        // cold CLI run over the same edited tree is the parity oracle.
        string reportEndpoint = fixture.PathOf(Web, "ReportEndpoint.cs");
        FixtureEdits.EditOnDisk(reportEndpoint, AddWhenFilterToSwallowingCatch);
        string after = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        CliResult coldEdited = await CliRunner.InvokeColdAsync(
            "check", fixture.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");

        // Assert — one edit, two rules, opposite answers: the filter-aware rule drops ReportEndpoint entirely
        // (leaving only ReportPublisher, the Web layer's other broad catcher, which this edit never touched),
        // while the plain catch ban keeps ReportEndpoint red at the very same single site, because a `when`
        // filter never suppresses the catch edge — it only changes which of the edge's sites are recorded
        // unfiltered (GRAMMAR §4.8). And the warm answer is byte-identical to the cold one.
        UnfilteredCatchSources(before)
            .ShouldBe(["MyApp.Web.ReportEndpoint", "MyApp.Web.ReportPublisher"], true);
        UnfilteredCatchSources(after)
            .ShouldBe(["MyApp.Web.ReportPublisher"], true);
        RuleStatusOf(before, "exceptions/no-general-catch")
            .ShouldBe("failed");
        RuleStatusOf(after, "exceptions/no-general-catch")
            .ShouldBe("failed");
        CatchSiteCount(after)
            .ShouldBe(1);
        after.NormalizedTrimmed()
            .ShouldNotBe(before.NormalizedTrimmed());
        after.NormalizedTrimmed()
            .ShouldBe(coldEdited.Out.NormalizedTrimmed());

        // …and the incremental store re-walked exactly the edited project (ReportEndpoint is in Web) plus its
        // reverse-dependent Domain.
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
    }

    [Fact]
    public async Task ArchCheck_SteadyStateNoDiskChange_ReadsAndReloadsNothing()
    {
        // Arrange — load, then promote every document past the racy window (backdate + one reconcile) so the
        // measured steady state is a pure O(stat) no-op (the steady-state case, driven through the tool).
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
        // Backdating is best-effort: a file that cannot be re-stamped stays racy and simply re-reads, which the
        // warmup below captures into the baseline, before the measured window opens.
        FixtureEdits.BackdateAllDocuments(loaded);
        await session.GetCurrentAsync(solutionPath, Ct); // warmup: content-verifies + promotes every document
        long readsBefore = session.SweepContentReads;
        long reloadsBefore = session.FullReloadCount;

        // Act — two more arch_check calls with disk untouched.
        string first = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();
        string second = (await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)).ShouldHaveTextContent();

        // Assert — the warm reconcile read no content and triggered no reload, the incremental store re-walked
        // nothing on the steady-state call, and the two responses are byte-identical.
        (session.SweepContentReads - readsBefore).ShouldBe(0);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        store.LastReExtractedProjects.ShouldBeEmpty();
        second.NormalizedTrimmed()
            .ShouldBe(first.NormalizedTrimmed());
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
        harness.Environment.SetVariable(LoadBearingEnvVars.DisableWarmWorkspace, "true");
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
        var first = harness.Client.CallToolAsync("arch_check", cancellationToken: Ct)
            .AsTask();
        var second = harness.Client.CallToolAsync("arch_status", cancellationToken: Ct)
            .AsTask();
        var results = await Task.WhenAll(first, second);

        // Assert — both succeeded with content.
        results.ShouldAllBe(result => result.IsError != true);
        results.Select(result => result.ShouldHaveTextContent())
            .ShouldAllBe(text => text.Length > 0);
    }

    [Fact]
    public async Task ArchCheck_SpecDllDeletedMidSession_ReturnsSameErrorAsColdCli()
    {
        // Arrange — bind to a throwaway copy of a spec DLL so deleting it cannot disturb the shared fixture.
        using TempDirectory temp = TestTempRoot.Fresh("warm-mcp-spec-copy");
        string tempSpec = temp.PathOf("CopiedSpec.dll");
        File.Copy(CliRunner.CleanSpecDll, tempSpec);

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            Binding(CliRunner.MyAppSolution, tempSpec), Ct);

        // Warm the session WITHOUT loading the spec: arch_graph opens the workspace (it is spec-free), so
        // the spec DLL stays unlocked and can be deleted to model a mid-session removal.
        CallToolResult graph = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);
        graph.IsError.ShouldNotBe(true);

        // Act — delete the bound spec DLL, then check against the still-warm workspace.
        File.Delete(tempSpec);
        CallToolResult check = await harness.Client.CallToolAsync("arch_check", cancellationToken: Ct);
        CliResult coldCheck = await CliRunner.InvokeColdAsync(
            "check", CliRunner.MyAppSolution, "--spec", tempSpec, "--json");
        coldCheck.ShouldRefuseWith();

        // Assert — the tool errors with exactly the text the cold CLI wrote to stderr in the same state.
        check.IsError.ShouldBe(true);
        check.ShouldHaveTextContent()
            .NormalizedTrimmed()
            .ShouldBe(coldCheck.Err.NormalizedTrimmed());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static McpServerBinding Binding(string? solution, string? spec)
    {
        string workingDirectory = solution is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(solution))!;
        return new McpServerBinding(solution, spec, workingDirectory);
    }

    // Turns HomeController's tokenless Save into one accepting a CancellationToken — the compliant signature,
    // so its member-shape red under async/accept-cancellation clears (its DocId changes as the parameter joins
    // the signature, and it drops out of the Task-returning subject's red set). Save has no callers, so the
    // signature change keeps MyApp.Web compiling.
    private static string AddCancellationTokenToSave(string source)
    {
        return source.Replace(
            "public System.Threading.Tasks.Task Save()",
            "public System.Threading.Tasks.Task Save(System.Threading.CancellationToken cancellationToken)");
    }

    // Flips IOrderFeed's registration from scoped to singleton in ServiceWiring — a singleton dependency injected
    // by a singleton is not captive, so the IOrderFeed edge drops out of di/no-captive-dependencies (GRAMMAR §4.7),
    // leaving only the transient IOrderFormatter red.
    private static string FlipOrderFeedToSingleton(string source)
    {
        return source.Replace("AddScoped<IOrderFeed, OrderFeed>", "AddSingleton<IOrderFeed, OrderFeed>");
    }

    // Adds a `when` filter to ReportEndpoint's blanket catch, using the method's own parameter so nothing new is
    // referenced. The catch edge and its §4.1 reference edge are minted exactly as before (GRAMMAR §4.8) — only
    // the recorded unfiltered-site set changes, which is what separates the two catch rules over this one site.
    private static string AddWhenFilterToSwallowingCatch(string source)
    {
        return source.Replace(
            "catch (System.Exception)",
            "catch (System.Exception) when (reportId > 0)");
    }

    // The member subjects (subjectMember DocIds) a member-subject rule reports red, keyed by rule id.
    private static IReadOnlyList<string> SubjectMembersOf(string checkJson, string ruleId)
    {
        return CheckJson.Violations(checkJson, ruleId)
            .Select(violation => violation.GetProperty("subjectMember")
                .GetString()!)
            .ToList();
    }

    // The injected-type targets reported red under the injection rule di/no-captive-dependencies (GRAMMAR §4.7).
    private static IReadOnlyList<string> CaptiveInjectedTargets(string checkJson)
    {
        return CheckJson.Violations(checkJson, "di/no-captive-dependencies")
            .Select(violation => violation.GetProperty("target")
                .GetString()!)
            .ToList();
    }

    // The number of member-use sites reported for the banned DateTime.Now read under time/inject-clock.
    private static int NowSiteCount(string checkJson)
    {
        return CheckJson.Violations(checkJson, "time/inject-clock")
            .Single(violation => violation.GetProperty("targetMember")
                .GetString() == "P:System.DateTime.Now")
            .GetProperty("sites")
            .GetArrayLength();
    }

    // The number of distinct catch violations ReportEndpoint contributes (one per (source, caught) type pair)
    // under exceptions/no-general-catch. Scoped to that source because the Web layer carries a second broad
    // catcher, ReportPublisher, whose rethrowing clause the plain catch ban reds too.
    private static int CatchViolationCount(string checkJson)
    {
        return CheckJson.Violations(checkJson, "exceptions/no-general-catch")
            .Count(violation => violation.GetProperty("source")
                .GetString() == "MyApp.Web.ReportEndpoint");
    }

    // The number of catch sites reported for the (ReportEndpoint, System.Exception) swallow under exceptions/no-general-catch.
    private static int CatchSiteCount(string checkJson)
    {
        return CheckJson.Violations(checkJson, "exceptions/no-general-catch")
            .Single(violation => violation.GetProperty("source")
                .GetString() == "MyApp.Web.ReportEndpoint")
            .GetProperty("sites")
            .GetArrayLength();
    }

    // A named rule's reported status — "passed", "failed" or "skipped".
    private static string RuleStatusOf(string checkJson, string ruleId)
    {
        return CheckJson.Rule(checkJson, ruleId)
            .GetProperty("status")
            .GetString()!;
    }

    // The catching types exceptions/no-unfiltered-catch reports, one per (source, caught) type pair.
    private static IReadOnlyList<string> UnfilteredCatchSources(string checkJson)
    {
        return CheckJson.Violations(checkJson, "exceptions/no-unfiltered-catch")
            .Select(violation => violation.GetProperty("source")
                .GetString()!)
            .ToList();
    }
}

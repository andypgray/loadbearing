using Shouldly;
using Xunit;
using Xunit.Sdk;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;
using Zphil.LoadBearing.Tests.TestSupport;
using Zphil.LoadBearing.Xunit;

namespace Zphil.LoadBearing.Tests.Xunit;

/// <summary>
///     The adapter's mechanics, isolated from the dogfood run: discovery uses rule IDs as display names, a
///     failed rule's <c>Assert.Fail</c> body is byte-identical (after normalization) to the CLI human
///     block, and a Quarantine tripwire without a diff is reported as skipped with the pinned reason. The two
///     inline specs replicate fixture rules verbatim because tests cannot reference the fixture spec
///     assemblies by design (<c>ReferenceOutputAssembly=false</c>); their driver classes are non-public, so
///     the test runner never discovers them — they are invoked directly.
///     Four facts cover the incomplete-model gate against the BrokenApp fixture: a partially-loaded
///     workspace fails <c>Workspace_LoadedCompletely</c> and skips the rule cases, and
///     <c>AllowWorkspaceDiagnostics</c> flips that pair over. Three more cover the opposite case against a
///     narrowing <c>.slnf</c> — a smaller model rather than a wrong one — where a rule whose subject the
///     filter kept still reports its verdict, a rule whose subject it dropped skips carrying the filter's
///     name, and <c>Workspace_LoadedCompletely</c> skips because it is the completeness claim itself.
/// </summary>
[Collection("Serial")]
public sealed class AdapterTests
{
    [Fact]
    public void RuleRows_UsesRuleIdsAsDisplayNames()
    {
        // The dogfood spec exercises all three postures, so discovery must surface each post-desugar rule
        // ID as its own display name — including the Quarantine scope's containment + tripwire children,
        // and the two rules the spec takes from the DotNetGuidance pack (a pack-declared rule is an
        // ordinary rule by the time the adapter sees it).
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<LoadBearingArchSpec>.RuleRows()
            .ToList();

        rows.Select(row => row.TestDisplayName)
            .ShouldBe(
            [
                "layering/core-no-roslyn",
                "layering/model-independent",
                "cli/no-stdout",
                "di/no-captive-dependencies",
                "di/no-service-locator",
                "di/no-buildserviceprovider",
                "mcp/tools-accept-cancellation",
                "mcp/tool-types-attributed",
                "roslyn/no-msbuildlocator-query",
                "mcp/no-blocking-waits",
                "mcp/no-path-assembly-loads",
                "naming/async-suffix",
                "mcp/warm-state-constructed-once",
                "roslyn/no-engine-types-on-seam",
                "xunit/leaf-adapter",
                "xunit/throws-setup-errors-only",
                "exceptions/no-swallowed-broad-catches",
                "exceptions/no-bare-bcl-throws",
                "packs/depends-on-core-only",
                "naming/interfaces",
                "model/constraint-nodes",
                "mcp/env-through-seam",
                "roslyn/msbuild-bootstrap/containment",
                "roslyn/msbuild-bootstrap/tripwire"
            ], true);
    }

    [Fact]
    public void RuleRows_SpecBuildFailure_CollapsesToSingleSentinelRow()
    {
        // A spec that fails validation at build time (a rule with no .Because) cannot enumerate its rules, so
        // discovery collapses to one sentinel row that lands red at run time — where the pipeline rebuild
        // rethrows the real SpecValidationException — rather than vanishing as a silent discovery diagnostic.
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<SpecBuildFailsSpec>.RuleRows()
            .ToList();

        ITheoryDataRow row = rows.ShouldHaveSingleItem();
        row.TestDisplayName.ShouldBe($"{nameof(SpecBuildFailsSpec)}: spec build failed");
    }

    [Fact]
    public async Task FailureText_MatchesCliHumanBlock()
    {
        // The CLI human block for the same rule + same solution is the oracle.
        CliResult cli = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        string expectedBlock = ExtractBlock(cli.Out, "layering/domain-independent");

        Exception? exception = await Record.ExceptionAsync(() => new InlineViolatedArchTests().Rule_Holds("layering/domain-independent"));

        var failure = exception.ShouldBeOfType<FailException>();
        failure.Message.NormalizedTrimmed()
            .ShouldBe(expectedBlock);
    }

    [Fact]
    public async Task Tripwire_WithoutDiff_Skips()
    {
        Exception? exception = await CaughtAsync(() => new InlineQuarantinedArchTests().Rule_Holds("legacy/billing/tripwire"));

        var skip = exception.ShouldBeOfType<SkipException>();
        // SkipException.ForSkip prefixes the reason with an internal dynamic-skip marker; the reason is the suffix.
        skip.Message.ShouldEndWith(ArchChecker.TripwireSkipReason);
    }

    [Fact]
    public async Task PartialWorkspace_FailsTheNamedTestCarryingTheDiagnostics()
    {
        // The CLI gate transposed to a test report: check gates and emits no verdict, so the adapter emits no
        // rule verdicts. The load failures ride the failure inline — a test report has no "warnings above" to
        // point at — and the opt-out is named, because that is the reader's next question.
        Exception? exception = await Record.ExceptionAsync(() => new BrokenAppArchTests().Workspace_LoadedCompletely());

        var failure = exception.ShouldBeOfType<FailException>();
        failure.Message.ShouldContain("BrokenApp.Contracts.csproj");
        failure.Message.ShouldContain("AllowWorkspaceDiagnostics");
    }

    [Fact]
    public async Task PartialWorkspace_SkipsEveryRuleCase()
    {
        // The bug this exists to close: a rule whose subject lived in the unloaded project selects nothing, an
        // empty subject passes, and the run goes green into CI's most-trusted signal. The reason is constant and
        // points at the named test rather than repeating the diagnostics once per rule.
        Exception? exception = await CaughtAsync(() => new BrokenAppArchTests().Rule_Holds(BrokenAppRuleId));

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.ShouldEndWith(IncompleteModelGate.AdapterSkipReason);
    }

    [Fact]
    public async Task PartialWorkspace_OptedIn_SkipsTheNamedTestRatherThanPassingFalsely()
    {
        // Opting in restores the rule verdicts, but a test called Workspace_LoadedCompletely cannot pass while
        // the load failures it is named for are real — so it skips, carrying them.
        Exception? exception = await CaughtAsync(() => new BrokenAppOptedInArchTests().Workspace_LoadedCompletely());

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.ShouldContain("BrokenApp.Contracts.csproj");
    }

    [Fact]
    public async Task PartialWorkspace_OptedIn_ReachesAVerdictOnTheRules()
    {
        // The opt-out's whole point: the rule is checked against the partial model as it loaded. Its subject and
        // target both live in projects that did load, so this is a real verdict, not an empty-subject pass.
        Exception? exception = await Record.ExceptionAsync(() => new BrokenAppOptedInArchTests().Rule_Holds(BrokenAppRuleId));

        exception.ShouldBeNull();
    }

    [Fact]
    public async Task NarrowingFilter_SkipsTheCompletenessClaimCarryingWhatWasNotChecked()
    {
        // A .slnf SolutionPath used to green every rule test over a subset: the adapter received the
        // unchecked projects and surfaced them nowhere. Nothing fails here — a narrowed universe is a smaller
        // true answer — but the one test whose name IS the completeness claim cannot pass while making it.
        Exception? exception = await CaughtAsync(() => new BillingOnlyArchTests().Workspace_LoadedCompletely());

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.Replace("\r\n", "\n")
            .ShouldContain(
                "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
                + "  MyApp.Domain/MyApp.Domain.csproj\n"
                + "  MyApp.Web/MyApp.Web.csproj\n"
                + "Rule verdicts come from the projects that loaded, but a test by this name cannot pass "
                + "while declared projects went unchecked; run the solution the filter references for the "
                + "whole answer.");
    }

    [Fact]
    public async Task NarrowingFilter_StillReportsEveryRuleCase()
    {
        // The line between a filter and a partial model, in one assertion: every verdict a filtered run
        // reached is real, so the rule cases report rather than skip the way BrokenApp's do above.
        Exception? exception = await Record.ExceptionAsync(() => new BillingOnlyArchTests().Rule_Holds(BillingOnlyRuleId));

        exception.ShouldBeNull();
    }

    [Fact]
    public async Task NarrowingFilter_RuleWhoseSubjectTheFilterDropped_SkipsRatherThanFailing()
    {
        // The other side of the row above: a verdict a filtered run could not reach is not a verdict, and
        // the adapter is where that lands hardest — an empty-subject red arrives as a failing named test
        // accusing the spec of naming a namespace the solution does have.
        Exception? exception = await CaughtAsync(() => new BillingOnlyWebArchTests().Rule_Holds(BillingOnlyWebRuleId));

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.ShouldEndWith(NarrowedUniverseNotice.RuleSkipReason("BillingOnly.slnf", 2));
    }

    [Fact]
    public void FindSolutionUp_FromTestOutput_ResolvesRepoSolution()
    {
        HelperAccessor.Call("Zphil.LoadBearing.slnx")
            .ShouldBe(RepoRoot.Solution);
    }

    [Fact]
    public void FindSolutionUp_MissingFile_ThrowsNamingFileAndOrigin()
    {
        var exception = Should.Throw<FileNotFoundException>(() => HelperAccessor.Call("no-such-file-f2.slnx"));
        exception.Message.ShouldBe($"Could not locate 'no-such-file-f2.slnx' walking up from '{AppContext.BaseDirectory}'.");
    }

    // The "FAIL <ruleId> …" block from the CLI human output: the header line plus its indented lines.
    private static string ExtractBlock(string humanOutput, string ruleId)
    {
        string[] lines = humanOutput.NormalizedLines()
            .Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith($"FAIL {ruleId}", StringComparison.Ordinal));
        if (start < 0) throw new InvalidOperationException($"No FAIL block for '{ruleId}' in:\n{humanOutput}");

        var block = new List<string> { lines[start] };
        for (int i = start + 1;
             i < lines.Length && lines[i]
                 .StartsWith("  ", StringComparison.Ordinal);
             i++)
            block.Add(lines[i]);

        return string.Join("\n", block);
    }

    // A verbatim inline copy of the fixture's layering/domain-independent rule, checked against the real
    // MyApp solution with no project excluded (as the CLI does for a by-path DLL spec), so its one rule
    // produces the identical violation block.
    private sealed class MyAppViolatedInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
            Layer web = arch.Layer("Web", "MyApp.Web.*");

            arch.Rule("layering/domain-independent")
                .Enforce(domain.MustNotReference(web))
                .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                .Fix("Define an abstraction in Domain and implement it in Web.");
        }
    }

    private sealed class InlineViolatedArchTests : ArchRuleTests<MyAppViolatedInlineSpec>
    {
        protected override string SolutionPath => CliRunner.MyAppSolution;
        protected override string? ExcludeProjectName => null;
    }

    // Reaches the protected FindSolutionUp helper without discovering a real test case: SolutionPath is
    // never read (the runner never sees this private class), so it throws if the pipeline ever touched it.
    private sealed class HelperAccessor : ArchRuleTests<MyAppViolatedInlineSpec>
    {
        protected override string SolutionPath => throw new NotSupportedException();

        public static string Call(string fileName)
        {
            return FindSolutionUp(fileName);
        }
    }

    // A quarantined scope over MyApp.Legacy.Billing — its desugared tripwire skips without a --diff-base.
    private sealed class MyAppQuarantinedInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.")
                .Because("Replacement scheduled; not worth stabilizing.");
        }
    }

    private sealed class InlineQuarantinedArchTests : ArchRuleTests<MyAppQuarantinedInlineSpec>
    {
        protected override string SolutionPath => CliRunner.MyAppSolution;
        protected override string? ExcludeProjectName => null;
    }

    // A spec that fails build-time validation: the rule carries a posture but no required .Because(...), so
    // ArchModelBuilder.Build throws SpecValidationException and RuleRows() takes its spec-build-failure branch.
    private sealed class SpecBuildFailsSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("area/rule")
                .Enforce(arch.Types.MustHavePrefix("I"));
        }
    }

    private const string BrokenAppRuleId = "layering/core-independent";

    // The fixture whose solution names three projects over a tree that holds two, so the load reports real
    // failures. Read in place from the test output, as the other drivers here read MyApp: the adapter only ever
    // reads the tree. BrokenApp.Contracts must stay absent — PartialLoadWorkspaceE2ETests guards that at arrange
    // time, and the two assertions on "BrokenApp.Contracts.csproj" below go red if the fixture ever heals.
    private static string BrokenAppSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PartialLoadSolutions", "BrokenApp", "BrokenApp.sln");

    // One rule spanning the two projects that DO load, so the opted-in run reaches a genuine verdict rather than
    // the empty-subject pass this whole gate exists to refuse.
    private class BrokenAppInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer core = arch.Layer("Core", "BrokenApp.Core.*");
            Layer web = arch.Layer("Web", "BrokenApp.Web.*");

            arch.Rule(BrokenAppRuleId)
                .Enforce(core.MustNotReference(web))
                .Because("Core holds the domain; the web host is a delivery detail.")
                .Fix("Declare an abstraction in Core and implement it in Web.");
        }
    }

    // The same spec under a second type purely to close ArchRuleTests<> over a different TSpec: the check run is
    // cached in a static per closed generic with no reset hook, so one spec type would hand both drivers the
    // first one's run and the AllowWorkspaceDiagnostics override would never be read.
    private sealed class BrokenAppOptedInInlineSpec : BrokenAppInlineSpec;

    private sealed class BrokenAppArchTests : ArchRuleTests<BrokenAppInlineSpec>
    {
        protected override string SolutionPath => BrokenAppSolution;
        protected override string? ExcludeProjectName => null;
    }

    private sealed class BrokenAppOptedInArchTests : ArchRuleTests<BrokenAppOptedInInlineSpec>
    {
        protected override string SolutionPath => BrokenAppSolution;
        protected override string? ExcludeProjectName => null;
        protected override bool AllowWorkspaceDiagnostics => true;
    }

    // Record.ExceptionAsync rethrows a SkipException rather than returning it, which xunit then applies to
    // the calling test — so a row that used it to assert on a skip reason was itself reported as skipped and
    // never ran its assertions. Catching by hand is what keeps the assertion below real.
    private static async Task<Exception?> CaughtAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private const string BillingOnlyRuleId = "layering/billing-independent";

    // The narrowing fixture, read in place from the test output for FilteredSolutionE2ETests' reason: the
    // filter reaches its solution by relative path, which a leased copy would strand. It selects the leaf of
    // the reference chain, so two of MyApp's three declared projects go unchecked; that class's arrange-time
    // guard is what keeps the two paths asserted above true.
    private static string BillingOnlyFilter =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FilteredSolutions", "BillingOnly.slnf");

    // Scoped to the one project the filter does select, so the rule case reaches a verdict over a subject
    // that really loaded rather than the empty-subject pass this whole arc exists to make visible.
    private sealed class BillingOnlyInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule(BillingOnlyRuleId)
                .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
                .Because("Billing must not reach up into the web layer.");
        }
    }

    private sealed class BillingOnlyArchTests : ArchRuleTests<BillingOnlyInlineSpec>
    {
        protected override string SolutionPath => BillingOnlyFilter;
        protected override string? ExcludeProjectName => null;
    }

    private const string BillingOnlyWebRuleId = "layering/web-independent";

    // The mirror of BillingOnlyInlineSpec: scoped to a project the filter drops rather than one it selects,
    // so the rule selects nothing and the run reaches no verdict for it — the case the skip exists for.
    private sealed class BillingOnlyWebInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule(BillingOnlyWebRuleId)
                .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
                .Because("The web layer talks to billing through an abstraction.");
        }
    }

    private sealed class BillingOnlyWebArchTests : ArchRuleTests<BillingOnlyWebInlineSpec>
    {
        protected override string SolutionPath => BillingOnlyFilter;
        protected override string? ExcludeProjectName => null;
    }
}

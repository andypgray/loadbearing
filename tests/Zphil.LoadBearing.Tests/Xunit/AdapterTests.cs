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
///     The last four facts cover the incomplete-model gate against the BrokenApp fixture: a partially-loaded
///     workspace fails <c>Workspace_LoadedCompletely</c> and skips the rule cases, and
///     <c>AllowWorkspaceDiagnostics</c> flips that pair over.
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
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<LoadBearingArchSpec>.RuleRows().ToList();

        rows.Select(row => row.TestDisplayName).ShouldBe(
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
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<SpecBuildFailsSpec>.RuleRows().ToList();

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
        failure.Message.NormalizedTrimmed().ShouldBe(expectedBlock);
    }

    [Fact]
    public async Task Tripwire_WithoutDiff_Skips()
    {
        Exception? exception = await Record.ExceptionAsync(() => new InlineQuarantinedArchTests().Rule_Holds("legacy/billing/tripwire"));

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
        Exception? exception = await Record.ExceptionAsync(() => new BrokenAppArchTests().Rule_Holds(BrokenAppRuleId));

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.ShouldEndWith(IncompleteModelGate.AdapterSkipReason);
    }

    [Fact]
    public async Task PartialWorkspace_OptedIn_SkipsTheNamedTestRatherThanPassingFalsely()
    {
        // Opting in restores the rule verdicts, but a test called Workspace_LoadedCompletely cannot pass while
        // the load failures it is named for are real — so it skips, carrying them.
        Exception? exception = await Record.ExceptionAsync(() => new BrokenAppOptedInArchTests().Workspace_LoadedCompletely());

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
    public void FindSolutionUp_FromTestOutput_ResolvesRepoSolution()
    {
        HelperAccessor.Call("Zphil.LoadBearing.slnx").ShouldBe(RepoRoot.Solution);
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
        string[] lines = humanOutput.NormalizedLines().Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith($"FAIL {ruleId}", StringComparison.Ordinal));
        if (start < 0) throw new InvalidOperationException($"No FAIL block for '{ruleId}' in:\n{humanOutput}");

        var block = new List<string> { lines[start] };
        for (int i = start + 1; i < lines.Length && lines[i].StartsWith("  ", StringComparison.Ordinal); i++)
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
            arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I"));
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
}

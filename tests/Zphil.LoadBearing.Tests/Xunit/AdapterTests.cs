using Shouldly;
using Xunit;
using Xunit.Sdk;
using Zphil.LoadBearing.ArchSpec;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Roslyn.Diagnostics;
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
///     name, and <c>Workspace_LoadedCompletely</c> skips because it is the completeness claim itself. Two
///     more cover the second way a whole load still covers less than the solution, against the polyglot
///     fixture: the same test skips naming the <c>.fsproj</c> and its reason, and the rule over the C#
///     project still reports.
/// </summary>
[Collection("Serial")]
public sealed class AdapterTests
{
    [Fact]
    public void RuleRows_UsesRuleIdsAsDisplayNames()
    {
        // The dogfood spec exercises all four postures, so discovery must surface each post-desugar rule
        // ID as its own display name — including the Quarantine scope's containment + tripwire children,
        // the Caution scope's tripwire alone, and the two rules the spec takes from the DotNetGuidance
        // pack (a pack-declared rule is an ordinary rule by the time the adapter sees it).
        IReadOnlyList<ITheoryDataRow> rows = ArchRuleTests<LoadBearingArchSpec>.RuleRows()
            .ToList();

        rows.Select(row => row.TestDisplayName)
            .ShouldBe(
            [
                "layering/core-no-roslyn",
                "layering/model-independent",
                "layering/no-circular-references",
                "layering/leaves-independent",
                "cli/no-stdout",
                "di/no-captive-dependencies",
                "di/no-service-locator",
                "di/no-buildserviceprovider",
                "mcp/tools-accept-cancellation",
                "mcp/tool-types-attributed",
                "mcp/tool-types-in-cli",
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
                "state/no-static-mutable",
                "packs/depends-on-core-only",
                "naming/interfaces",
                "model/constraint-nodes",
                "model/reified-nodes-immutable",
                "api/core-front-door",
                "api/extraction-front-door",
                "api/host-front-door",
                "packaging/core-netstandard-only",
                "packaging/core-carries-nothing",
                "packaging/shipping-locks-restore",
                "packaging/only-the-four-ship",
                "mcp/env-through-seam",
                "roslyn/msbuild-bootstrap/containment",
                "roslyn/msbuild-bootstrap/tripwire",
                "model/prose-fragments/tripwire"
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
    public async Task CautionTripwire_WithoutDiff_Skips()
    {
        // A caution's only rule is its tripwire, and a test run has no diff to hand it, so the case is a
        // permanent skip here: present in the explorer, naming the scope, never firing. That is the
        // adapter's nature and not a defect — the verdict a caution wants is `check --diff-base`'s, which
        // is a pull-request concern.
        Exception? exception = await CaughtAsync(() => new InlineCautionedArchTests().Rule_Holds("domain/retry-budget/tripwire"));

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.ShouldEndWith(ArchChecker.CautionTripwireSkipReason);
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
        // The bug this exists to close: the run measures a codebase missing whole projects, nothing says so, and
        // the run goes green into CI's most-trusted signal. The reason is constant and points at the named test
        // rather than repeating the diagnostics once per rule.
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
        skip.Message.NormalizedLines()
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
    public async Task PolyglotSolution_SkipsTheCompletenessClaimNamingWhatItCouldNotReach()
    {
        // The adapter received the unsupported projects and rendered them nowhere, so a polyglot solution's
        // rule tests went green with no coverage statement anywhere — while check and status both stamped
        // one. Nothing here failed: this is a smaller true answer, like a filter's, and the one test whose
        // name IS the completeness claim is the one that cannot make it.
        Exception? exception = await CaughtAsync(() => new PolyglotArchTests().Workspace_LoadedCompletely());

        var skip = exception.ShouldBeOfType<SkipException>();
        skip.Message.NormalizedLines()
            .ShouldContain(
                "1 project the solution declares was not surveyed:\n"
                + "  PolyglotApp.Fs/PolyglotApp.Fs.fsproj — not a C# project\n"
                + "Rule verdicts come from the projects this product can read, but a test by this name "
                + "cannot pass while the solution declares projects the model never held.");
    }

    [Fact]
    public async Task PolyglotSolution_StillReportsEveryRuleCase()
    {
        // The line between an unreadable project and a broken model, in one assertion: the C# project loaded
        // whole and the rule over it reached a real verdict, so the rule cases report rather than skip the
        // way BrokenApp's do.
        Exception? exception = await Record.ExceptionAsync(() => new PolyglotArchTests().Rule_Holds(PolyglotRuleId));

        exception.ShouldBeNull();
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
    // Reached through its static Call below, which is why it is never instantiated.
    // ReSharper disable once ClassNeverInstantiated.Local
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

    // A cautioned scope over MyApp.Domain — its one and only child is the tripwire, so on this adapter the
    // whole scope is a skip.
    private sealed class MyAppCautionedInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("domain/retry-budget")
                .Caution(arch.Namespace("MyApp.Domain.*"))
                .Dragons("The back-off table is tuned against production, not first principles. Keep the timings.")
                .Because("Every caller depends on the exact timings.");
        }
    }

    private sealed class InlineCautionedArchTests : ArchRuleTests<MyAppCautionedInlineSpec>
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

    private const string PolyglotRuleId = "naming/interfaces";

    // The one bed in the suite declaring a project no extractor reaches: a real .fsproj, not a renamed
    // .csproj, beside one ordinary C# project. Read in place from the test output like the two drivers
    // above — FixtureRestorer restores every solution under TestSolutions/ at assembly startup, so the C#
    // project loads whole and the skip below is about coverage rather than a broken model.
    private static string PolyglotSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "PolyglotApp", "PolyglotApp.slnx");

    // A verbatim inline copy of the fixture spec's one rule, which holds over the C# project — so the rule
    // case below reaches a genuine verdict rather than an empty-subject pass, and the skip is provably about
    // the .fsproj alone.
    private sealed class PolyglotInlineSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule(PolyglotRuleId)
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("PolyglotApp.Core.*").MustHavePrefix("I"))
                .Because("House naming convention; agents grep by I-prefix.");
        }
    }

    private sealed class PolyglotArchTests : ArchRuleTests<PolyglotInlineSpec>
    {
        protected override string SolutionPath => PolyglotSolution;
        protected override string? ExcludeProjectName => null;
    }
}

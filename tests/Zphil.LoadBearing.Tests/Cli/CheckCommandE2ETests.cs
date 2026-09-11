using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>check</c> runs against the real MyApp fixture solution (each loads a fresh
///     workspace). The violated spec is the acceptance box — one failing rule showing ID, because,
///     fix, and <c>file:line</c> together — plus the quarantine containment (uncaptured hard red + facade
///     green + tripwire skip), the JSON golden pin, and the SARIF golden pin (with its <c>--json</c>
///     stdout-purity guard); the clean spec exits 0.
///     <para>
///         The <c>--rules</c> rows pin the narrowing knob: it decides what <em>runs</em>, so the report, the
///         summary counts and the 0/1 verdict are all the subset's, and the human stamp (or the document's
///         <c>rulesFilter</c>) is what stops a green subset of a red spec reading as a green solution. A
///         filter matching no rule refuses before extraction, since rule IDs come from the spec model.
///     </para>
///     <para>
///         The grain rows pin the other knob, and the two are independent: <c>--rules</c> narrows the
///         <em>subject</em> (which rules are evaluated at all), while <c>--overview</c> and
///         <c>--skeleton</c> coarsen the <em>grain</em> (how much of each evaluated rule is written). Every
///         rung is a whole verdict over the same rules — which is what makes an over-budget MCP call safe to
///         degrade automatically — full grain stamps no <c>grain</c> key, and the per-rule
///         <c>violationCount</c> and per-violation <c>siteCount</c> ride every rung, so the keys a consumer
///         scripts against never depend on the grain the report landed on.
///     </para>
/// </summary>
[Collection("Serial")]
public sealed class CheckCommandE2ETests
{
    // The violated-spec report, run once for the whole class. A dozen facts below assert different slices
    // of one report from a byte-identical command line, over paths that are run-stable statics and a fixture
    // tree no fact here mutates — so re-running the check per fact bought twelve identical extractions and
    // no isolation. The Lazy defers the run until the first fact that needs it and shares the fault if it
    // throws; every other fact (clean spec, --sarif, --rules, missing spec) still runs its own command line.
    private static readonly Lazy<Task<CliResult>> ViolatedHuman = new(() =>
        CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll));

    private static readonly Lazy<Task<CliResult>> ViolatedJson = new(() =>
        CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json"));

    [Fact]
    public async Task Check_ViolatedSpec_ExitsOneWithAllFourComponents()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations("FAIL layering/domain-independent");
        result.Out.ShouldContain("because: Domain is UI-agnostic; transaction boundaries live in services.");
        result.Out.ShouldContain("fix: Define an abstraction in Domain and implement it in Web.");
        result.Out.ShouldContain(
            "MyApp.Domain/OrderService.cs:9 — MyApp.Domain.OrderService references MyApp.Web.HomeController");
    }

    [Fact]
    public async Task Check_ViolatedSpec_RatchetsMigrateRuleWithGrandfatheredInvoiceAndRedHome()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // HomeController's DataTable is new code in the old pattern — red.
        result.Out.ShouldContain("FAIL data-access/no-inline-sql");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:24 — MyApp.Web.HomeController references System.Data.DataTable");
        result.Out.ShouldContain("grandfathered: 1 (baselined; run 'loadbearing status' for burndown)");
        // InvoiceController's DataTable is grandfathered by the conventional baseline — suppressed, not red.
        result.Out.ShouldNotContain("MyApp.Web.InvoiceController references System.Data.DataTable");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsMemberUseRuleWithUsesLinesAndInitHint()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The member-use half of the report (GRAMMAR §4.5): the layer-voice sentence, a 'uses' line per banned
        // read at its file:line, and — uncaptured member-level Migrate — the same --init hint as any ratcheted rule.
        result.Out.ShouldContain("FAIL time/inject-clock — The Web layer must not use `DateTime.Now` or `DateTime.UtcNow`.");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:32 — MyApp.Web.HomeController uses System.DateTime.Now");
        result.Out.ShouldContain("MyApp.Web/HomeController.cs:37 — MyApp.Web.HomeController uses System.DateTime.UtcNow");
        result.Out.ShouldContain(
            "hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    [Fact]
    public async Task Check_ViolatedSpec_QuarantineContainmentRedInteriorGreenFacadeTripwireSkips()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // Uncaptured containment (its baseline path is deliberately uncommitted) → interior refs are hard red.
        result.Out.ShouldContain("FAIL legacy/billing/containment");
        result.Out.ShouldContain(
            "MyApp.Web/InvoiceController.cs:9 — MyApp.Web.InvoiceController references MyApp.Legacy.Billing.BillingCalculator");
        result.Out.ShouldContain("fix: use `IBillingFacade`");
        result.Out.ShouldContain(
            "hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
        // The facade path (HomeController → IBillingFacade) is the sanctioned surface — green, never listed.
        result.Out.ShouldNotContain("MyApp.Web.HomeController references MyApp.Legacy.Billing.IBillingFacade");
        // The tripwire skips without a --diff-base.
        result.Out.ShouldContain("skip legacy/billing/tripwire");
        result.Out.ShouldContain(
            "skipped: Tripwire: no diff context — run 'loadbearing check --diff-base <ref>' to check changed files against this quarantined scope.");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsCatchRuleWithCatchesLineAndInitHint()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The catch half of the report (GRAMMAR §4.8): the caught-type sentence, a 'catches' line at the
        // catch site's file:line, and — uncaptured catch-level Migrate — the same --init hint as any ratcheted rule.
        result.Out.ShouldContain("FAIL exceptions/no-general-catch — The Web layer must not catch `Exception`.");
        result.Out.ShouldContain(
            "MyApp.Web/ReportEndpoint.cs:15 — MyApp.Web.ReportEndpoint catches System.Exception");
        result.Out.ShouldContain(
            "hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsThrowRuleWithThrowsLineAndAllowsDomainException()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The throw half of the report (GRAMMAR §4.8): a strict Enforce allow-list. OrderApproval's BCL throw
        // is red at its site; its sanctioned OrderRuleViolation throw is green — allowed, so never listed.
        result.Out.ShouldContain("FAIL exceptions/domain-throws-domain — The Domain layer must throw only `OrderRuleViolation`.");
        result.Out.ShouldContain(
            "MyApp.Domain/OrderApproval.cs:17 — MyApp.Domain.OrderApproval throws System.InvalidOperationException");
        result.Out.ShouldNotContain("throws MyApp.Domain.OrderRuleViolation");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsUnfilteredCatchRuleAndSparesTheFilteredCatch()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The filter-aware catch half of the report (GRAMMAR §4.8): a union subject speaks in union voice, and
        // the rule reads the sites extraction recorded as unfiltered. ReportEndpoint's and ReportPublisher's
        // blanket catches spell no `when` filter, so both are red at the very sites exceptions/no-general-catch
        // reds — a second rule over the same two edges, each with its own identity.
        result.Out.ShouldContain(
            "FAIL exceptions/no-unfiltered-catch — The Web or Domain layers must not catch `Exception` without a `when` filter.");
        result.Out.ShouldContain(
            "MyApp.Web/ReportEndpoint.cs:15 — MyApp.Web.ReportEndpoint catches System.Exception");
        result.Out.ShouldContain(
            "MyApp.Web/ReportPublisher.cs:22 — MyApp.Web.ReportPublisher catches System.Exception");
        // The green half, stated as an absence: RetryPolicy catches the identical type under the identical
        // subject, and its `when` filter keeps it out of the evidence entirely — no site line, no mention.
        result.Out.ShouldNotContain("RetryPolicy");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsSwallowRuleAndSparesTheRethrowingCatch()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The rethrow-aware catch half of the report (GRAMMAR §4.8), and the axis differentiator end to end.
        // ReportPublisher's catch is red under BOTH rules above — same type, same lack of filter — and green
        // here, because its clause ends in `throw;` and suppresses nothing. Its absence from this one block is
        // therefore the whole difference between the three catch verbs, read off a single report.
        string block = RuleBlock(result.Out, "exceptions/no-swallowed-catch");
        block.ShouldContain("The Web or Domain layers must not swallow `Exception`.");
        block.ShouldContain("MyApp.Web/ReportEndpoint.cs:15 — MyApp.Web.ReportEndpoint catches System.Exception");
        block.ShouldNotContain("ReportPublisher");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsThrowBanRuleAlongsideTheThrowAllowList()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The ban polarity beside the allow-list (GRAMMAR §4.8): two rules red at the SAME throw site, each with
        // its own identity. The 'throws' evidence line is shared text with exceptions/domain-throws-domain, so
        // what tells the two blocks apart is the prose — this rule names the one type it bans, not the set it
        // permits — and those are the pins.
        result.Out.ShouldContain(
            "FAIL exceptions/no-bare-bcl-throw — The Domain layer must not throw `InvalidOperationException`.");
        result.Out.ShouldContain(
            "because: InvalidOperationException tells a caller nothing it can dispatch on; the domain has its own exception for rule failures.");
        result.Out.ShouldContain("fix: Throw OrderRuleViolation instead of System.InvalidOperationException.");
        result.Out.ShouldContain(
            "MyApp.Domain/OrderApproval.cs:17 — MyApp.Domain.OrderApproval throws System.InvalidOperationException");
    }

    [Fact]
    public async Task Check_ViolatedSpec_ReportsExposeRuleWithExposesLinesAndInitHint()
    {
        CliResult result = await ViolatedHuman.Value;

        result.ShouldReportViolations();
        // The exposure half of the report (GRAMMAR §4.9): the layer-voice sentence, an 'exposes' line per public
        // signature that surfaces the banned type, and — uncaptured Migrate — the same --init hint as any ratcheted
        // rule. Both controllers red: InvoiceController is grandfathered for its DataTable *reference* under
        // data-access/no-inline-sql, but baselines are per-rule, so the byte-identical exposure edge reds here.
        result.Out.ShouldContain("FAIL api/return-dtos — The Web layer must not expose `DataTable`.");
        result.Out.ShouldContain(
            "MyApp.Web/HomeController.cs:24 — MyApp.Web.HomeController exposes System.Data.DataTable");
        result.Out.ShouldContain(
            "MyApp.Web/InvoiceController.cs:14 — MyApp.Web.InvoiceController exposes System.Data.DataTable");
        result.Out.ShouldContain(
            "hint: no baseline captured for this rule; run 'loadbearing baseline --init' to grandfather existing violations");
    }

    [Fact]
    public async Task Check_CleanSpec_ExitsZero()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll);

        result.ShouldSucceed();
    }

    [Fact]
    public async Task Check_ViolatedSpecJson_MatchesGolden()
    {
        CliResult result = await ViolatedJson.Value;

        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check.json");
    }

    [Fact]
    public async Task Check_ViolatedSpecJson_StampsNoGrain()
    {
        // The negative control that keeps the grain stamp meaningful: a full report says nothing about
        // grain, so a document that names one is always a coarsened one, never the complete report.
        CliResult result = await ViolatedJson.Value;

        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("grain", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_ViolatedSpecOverviewJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--overview");

        // Assert — the whole report at coarser grain: every rule and every violation still here with its
        // prose and its edge, each violation's sites elided down to the siteCount that rides every grain,
        // and the grain stamped so a reader knows which document this is.
        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check-overview.json");
    }

    [Fact]
    public async Task Check_ViolatedSpecSkeletonJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--skeleton");

        // Assert — the verdict alone: every rule with its prose, status, baseline and warnings, its
        // violations elided down to the violationCount that rides every grain. Still a verdict, which is
        // what distinguishes it from status's burndown over the same rules.
        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check-skeleton.json");
    }

    [Fact]
    public async Task Check_ViolatedSpecIndexJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--index");

        // Assert — the ladder's floor: a verdict per rule ID, with the prose gone and everything a next call
        // needs still here. It is not a smaller skeleton for the same reader — it is what a client whose
        // channel cannot hold the skeleton gets instead of a document cut mid-array.
        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check-index.json");
    }

    [Fact]
    public async Task Check_OverviewAndSkeletonTogether_TakesTheCoarserGrain()
    {
        // Arrange & Act — the two name a floor on detail rather than competing modes, so asking for both is
        // not a conflict to refuse over. Same rule as graph's, from the same mapper.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--overview",
            "--skeleton");

        // Assert
        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check-skeleton.json");
    }

    [Fact]
    public async Task Check_EveryGrainFlagTogether_TakesTheCoarsestOfThem()
    {
        // Arrange & Act — the coarsest-wins rule holds however many flags arrive, which is what keeps a
        // third rung from turning the mapper into a precedence puzzle.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--overview",
            "--skeleton", "--index");

        // Assert
        result.ShouldReportViolations();
        result.Out.ShouldMatchGolden("violated-check-index.json");
    }

    [Fact]
    public async Task Check_SkeletonJson_ReportsAPassingRulesElidedViolationsAsZeroRatherThanOmittingThem()
    {
        // Arrange — an elided array must not read as an empty one, and the case that proves it is the rule
        // with nothing to elide: a passing rule renders violationCount 0, so "not rendered at this grain"
        // and "none found" stay distinguishable at the one place they would otherwise collapse.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--skeleton");

        // Act
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement passing = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .First(rule => rule.GetProperty("status")
                .GetString() == "passed");

        // Assert
        passing.TryGetProperty("violations", out _)
            .ShouldBeFalse();
        passing.GetProperty("violationCount")
            .GetInt32()
            .ShouldBe(0);
    }

    [Fact]
    public async Task Check_FullJson_EveryRuleAndEveryViolationCarryTheCountBesideItsExpansion()
    {
        // The count is the key a consumer scripts against, so rendering the expansion must never displace
        // it: at full grain both sit side by side and the count is the array's length — which also keeps
        // grandfathered violations out of the count exactly as they stay out of the array. Swept over every
        // rule and every violation so a future grain change cannot drop either count from any of them.
        CliResult result = await ViolatedJson.Value;

        result.ShouldReportViolations();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        foreach (JsonElement rule in document.RootElement.GetProperty("rules")
                     .EnumerateArray())
        {
            string id = rule.GetProperty("id")
                .GetString()!;
            List<string> keys = rule.EnumerateObject()
                .Select(field => field.Name)
                .ToList();
            keys.ShouldContain("violationCount", id);
            keys.ShouldContain("violations", id);
            rule.GetProperty("violationCount")
                .GetInt32()
                .ShouldBe(rule.GetProperty("violations")
                    .GetArrayLength(), id);

            foreach (JsonElement violation in rule.GetProperty("violations")
                         .EnumerateArray())
            {
                List<string> violationKeys = violation.EnumerateObject()
                    .Select(field => field.Name)
                    .ToList();
                violationKeys.ShouldContain("siteCount", id);
                violationKeys.ShouldContain("sites", id);
                violation.GetProperty("siteCount")
                    .GetInt32()
                    .ShouldBe(violation.GetProperty("sites")
                        .GetArrayLength(), id);
            }
        }
    }

    [Fact]
    public async Task Check_SkeletonJson_EveryRuleKeepsTheCountWhenTheElisionDropsTheArray()
    {
        // The same sweep at the coarse grain: the elision drops the array and only the array. Together with
        // the full-grain sweep above this pins the per-rule key set at both ends of the ladder, so no
        // future grain change can drop the count silently from either.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--skeleton");

        result.ShouldReportViolations();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        foreach (JsonElement rule in document.RootElement.GetProperty("rules")
                     .EnumerateArray())
        {
            string id = rule.GetProperty("id")
                .GetString()!;
            List<string> keys = rule.EnumerateObject()
                .Select(field => field.Name)
                .ToList();
            keys.ShouldContain("violationCount", id);
            keys.ShouldNotContain("violations", id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("--overview")]
    [InlineData("--skeleton")]
    public async Task Check_JsonAtEveryGrain_ADefensiveReadOfAFailedRulesCountNeverAnswersZero(string? grainFlag)
    {
        // The field-test repro: a consumer scripting the Python idiom rule.get('violationCount', 0) over a
        // fine-grain report read 0 for rules that had failed, because the count key used to substitute for
        // the rendered array. Absent-means-zero is the ordinary defensive read, so the count must be present
        // and non-zero for every failed rule at every grain — that is what makes the read safe.
        string[] arguments = grainFlag is null
            ? ["check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json"]
            : ["check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", grainFlag];

        CliResult result = await CliRunner.InvokeAsync(arguments);

        result.ShouldReportViolations();
        result.Out.ShouldReportViolationCount("layering/domain-independent", 2);
        using JsonDocument document = result.ShouldHaveJsonStdout();
        List<JsonElement> failed = document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Where(rule => rule.GetProperty("status")
                .GetString() == "failed")
            .ToList();

        failed.ShouldNotBeEmpty();
        foreach (JsonElement rule in failed)
        {
            string id = rule.GetProperty("id")
                .GetString()!;
            rule.TryGetProperty("violationCount", out JsonElement count)
                .ShouldBeTrue(id);
            count.GetInt32()
                .ShouldBeGreaterThan(0, id);
        }
    }

    [Fact]
    public async Task Check_HumanOutput_IgnoresTheGrainFlags()
    {
        // Arrange — grain is a property of the JSON document alone: a terminal has no response budget to
        // overrun, and the human block is what a developer reads to fix something.
        CliResult plain = await ViolatedHuman.Value;

        // Act
        CliResult skeleton = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--skeleton");

        // Assert
        skeleton.ShouldReportViolations();
        skeleton.Out.NormalizedTrimmed()
            .ShouldBe(plain.Out.NormalizedTrimmed());
    }

    [Fact]
    public async Task Check_ViolatedSpecSarif_MatchesGolden()
    {
        // The SARIF render target over the same result model: one result per violation site, red errors and
        // grandfathered notes, solution-relative paths. The golden is temp-path-independent because every URI
        // is resolved against SRCROOT, so the temp file the run writes to never leaks into the document.
        using TempDirectory temp = TestTempRoot.Fresh("check-sarif");
        string sarifPath = temp.PathOf("violated-check.sarif");

        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--sarif", sarifPath);

        result.ShouldReportViolations();
        File.ReadAllText(sarifPath)
            .ShouldMatchGolden("violated-check.sarif");
    }

    [Fact]
    public async Task Check_ViolatedSpecJsonWithSarif_StdoutStaysPureJson()
    {
        // --json owns stdout. --sarif writes its report to the file, and the human-mode `wrote <path>` line is
        // suppressed under --json, so a hook still parses a single JSON document off stdout. The parse below is
        // the whole claim: it rejects trailing content, so a leaked wrote line cannot hide behind the document.
        using TempDirectory temp = TestTempRoot.Fresh("check-sarif");
        string sarifPath = temp.PathOf("violated-check.sarif");

        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--sarif", sarifPath);

        result.ShouldReportViolations();
        using JsonDocument _ = result.ShouldHaveJsonStdout();
        File.Exists(sarifPath)
            .ShouldBeTrue();
        File.ReadAllText(sarifPath)
            .ShouldContain("\"$schema\"");
    }

    [Fact]
    public async Task Check_MissingSpecFile_ExitsTwoWithMessage()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", "does-not-exist.dll");

        result.ShouldRefuseWith("was not found");
    }

    [Fact]
    public async Task Check_RulesSelectingOnePassingRule_StampsTheFilterAndExitsZero()
    {
        // Act — one green rule picked out of a spec with twenty-four red ones.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--rules", "layering/billing-independent");

        // Assert — exit 0, because the rules that were not selected were not run. That is the whole hazard the
        // stamp exists for: a green subset of a red spec looks exactly like a green solution without it.
        result.ShouldSucceed(
            "Checking 1 of 27 rules matching 'layering/billing-independent'; the verdict below covers only those, "
            + "so a clean result here is not a clean solution.");
        result.Out.ShouldNotContain("layering/domain-independent");
    }

    [Fact]
    public async Task Check_RulesSelectingAnAreaGlob_CountsTheSubsetAndStillExitsOne()
    {
        // Act — an area glob over the five exceptions/* rules, all of them red.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--rules", "exceptions/*");

        // Assert — the exit contract is untouched: narrowing changes what runs, never what a violation means.
        result.ShouldReportViolations(
            "Checking 5 of 27 rules matching 'exceptions/*'; the verdict below covers only those, so a clean "
            + "result here is not a clean solution.",
            "FAIL exceptions/no-general-catch",
            "FAIL exceptions/no-bare-bcl-throw");
        // The rules outside the filter are absent entirely — --rules narrows what runs, not what is displayed.
        result.Out.ShouldNotContain("layering/domain-independent");
        result.Out.ShouldNotContain("legacy/billing/containment");
    }

    [Fact]
    public async Task Check_RulesSubsetJson_RecordsTheFilterAndCountsOnlyWhatRan()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--rules", "exceptions/*");

        // Assert — the stamp never reaches stdout under --json; the document carries the same fact in
        // rulesFilter, and the summary counts the five rules that ran rather than the twenty-seven that exist.
        result.ShouldReportViolations();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement root = document.RootElement;

        root.GetProperty("rulesFilter")
            .EnumerateArray()
            .Select(glob => glob.GetString())
            .ShouldBe(["exceptions/*"]);
        root.GetProperty("rules")
            .GetArrayLength()
            .ShouldBe(5);
        root.GetProperty("summary")
            .GetProperty("rulesChecked")
            .GetInt32()
            .ShouldBe(5);
    }

    [Fact]
    public async Task Check_UnfilteredJson_OmitsTheRulesFilterEntirely()
    {
        // Act
        CliResult result = await ViolatedJson.Value;

        // Assert — the slot is additive: absent, not empty, so a whole-spec document is byte-identical to the
        // one before --rules existed (which the golden above pins).
        result.ShouldReportViolations();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("rulesFilter", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_RulesMatchingNothing_RefusesBeforeExtractionAndListsTheAvailableRuleIds()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--rules", "nope/*");

        // Assert — refusing beats checking nothing, which would exit 0 and read as a clean solution. The IDs
        // come from the spec model, so this costs no codebase walk to say.
        result.ShouldRefuseWith(
            "No rule matched 'nope/*'. Available rule IDs:",
            "layering/domain-independent",
            "legacy/billing/tripwire");
        result.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_RulesAsAStringifiedArray_RefusesOnTheShapeAndOmitsTheRoster()
    {
        // Act — what a client sends when it serializes a JSON array into the string parameter the schema
        // advertises. The ID inside the brackets is a real one, which is what made the roster read as a
        // contradiction: it named back the very ID the lead said nothing matched.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll,
            "--rules", """["layering/domain-independent"]""");

        // Assert — the roster's absence is the point. Every name the reader could want is inside their own
        // brackets, so the missing information is the shape, and the advice echoes the value to paste back.
        result.ShouldRefuseWith(
            "No rule matched '[\"layering/domain-independent\"]'. That is a JSON array written as text; "
            + "pass the globs as one semicolon-separated string: 'layering/domain-independent'.");
        result.Err.ShouldNotContain("Available rule IDs");
        result.Out.ShouldBeEmpty();
    }

    // One rule's block out of the human report: its marker line plus the indented lines under it. Needed
    // wherever an assertion is an ABSENCE — a type named by one rule and not another is invisible to a
    // whole-report ShouldNotContain.
    private static string RuleBlock(string report, string ruleId)
    {
        string[] lines = report.NormalizedTrimmed()
            .Split('\n');
        int start = Array.FindIndex(lines, line => line.Contains($" {ruleId} —", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, $"the report names no rule {ruleId}");

        int end = start + 1;
        while (end < lines.Length && lines[end]
                   .StartsWith("  ", StringComparison.Ordinal)) end++;

        return string.Join("\n", lines[start..end]);
    }
}

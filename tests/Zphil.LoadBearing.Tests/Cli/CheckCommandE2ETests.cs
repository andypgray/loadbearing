using System.Text.Json;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>check</c> runs against the real MyApp fixture solution (each loads a fresh
///     workspace). The violated spec is the acceptance box — one failing rule showing ID, because,
///     fix, and <c>file:line</c> together — plus the quarantine containment (uncaptured hard red + facade
///     green + tripwire skip), the JSON golden pin, and the SARIF golden pin (with its <c>--json</c>
///     stdout-purity guard); the clean spec exits 0.
/// </summary>
[Collection("Serial")]
public sealed class CheckCommandE2ETests
{
    [Fact]
    public async Task Check_ViolatedSpec_ExitsOneWithAllFourComponents()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldReportViolations("FAIL layering/domain-independent");
        result.Out.ShouldContain("because: Domain is UI-agnostic; transaction boundaries live in services.");
        result.Out.ShouldContain("fix: Define an abstraction in Domain and implement it in Web.");
        result.Out.ShouldContain(
            "MyApp.Domain/OrderService.cs:9 — MyApp.Domain.OrderService references MyApp.Web.HomeController");
    }

    [Fact]
    public async Task Check_ViolatedSpec_RatchetsMigrateRuleWithGrandfatheredInvoiceAndRedHome()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

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
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");

        result.ShouldReportViolations();
        Normalize(result.Out).ShouldBe(Normalize(Golden()));
    }

    [Fact]
    public async Task Check_ViolatedSpecSarif_MatchesGolden()
    {
        // The SARIF render target over the same result model: one result per violation site, red errors and
        // grandfathered notes, solution-relative paths. The golden is temp-path-independent because every URI
        // is resolved against SRCROOT, so the temp file the run writes to never leaks into the document.
        string sarifPath = Path.Combine(Path.GetTempPath(), $"loadbearing-sarif-{Guid.NewGuid():N}.sarif");
        try
        {
            CliResult result = await CliRunner.InvokeAsync(
                "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--sarif", sarifPath);

            result.ShouldReportViolations();
            Normalize(File.ReadAllText(sarifPath)).ShouldBe(Normalize(GoldenSarif()));
        }
        finally
        {
            File.Delete(sarifPath);
        }
    }

    [Fact]
    public async Task Check_ViolatedSpecJsonWithSarif_StdoutStaysPureJson()
    {
        // --json owns stdout. --sarif writes its report to the file, and the human-mode `wrote <path>` line is
        // suppressed under --json, so a hook still parses a single JSON document off stdout.
        string sarifPath = Path.Combine(Path.GetTempPath(), $"loadbearing-sarif-{Guid.NewGuid():N}.sarif");
        try
        {
            CliResult result = await CliRunner.InvokeAsync(
                "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json", "--sarif", sarifPath);

            result.ShouldReportViolations();
            using JsonDocument _ = result.ShouldHaveJsonStdout();
            result.Out.ShouldNotContain("wrote");
            File.Exists(sarifPath).ShouldBeTrue();
            File.ReadAllText(sarifPath).ShouldContain("\"$schema\"");
        }
        finally
        {
            File.Delete(sarifPath);
        }
    }

    [Fact]
    public async Task Check_MissingSpecFile_ExitsTwoWithMessage()
    {
        CliResult result = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", "does-not-exist.dll");

        result.ShouldRefuseWith("was not found");
    }

    private static string Golden()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", "violated-check.json"));
    }

    private static string GoldenSarif()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", "violated-check.sarif"));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }

    // One rule's block out of the human report: its marker line plus the indented lines under it. Needed
    // wherever an assertion is an ABSENCE — a type named by one rule and not another is invisible to a
    // whole-report ShouldNotContain.
    private static string RuleBlock(string report, string ruleId)
    {
        string[] lines = Normalize(report).Split('\n');
        int start = Array.FindIndex(lines, line => line.Contains($" {ruleId} —", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, $"the report names no rule {ruleId}");

        int end = start + 1;
        while (end < lines.Length && lines[end].StartsWith("  ", StringComparison.Ordinal)) end++;

        return string.Join("\n", lines[start..end]);
    }
}
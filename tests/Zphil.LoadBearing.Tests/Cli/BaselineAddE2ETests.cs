using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline --add</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />, one per fact) — the ratchet's escape valve (DESIGN.md §13(d)).
///     Pins the whole valve: a Migrate <c>--add</c> appends exactly one attributed entry as a one-line
///     diff while same-rule and other-rule bystanders stay red; a Freeze-containment <c>--add</c>
///     grandfathers a new inbound edge and turns the rule green; a present entry only has its attribution
///     updated; the attribution survives an <c>--init</c>/<c>--accept-reductions</c> round-trip
///     byte-for-byte; the four refusals exit 2 with their pinned messages; and — the point of the phase —
///     no <c>check</c>/<c>status</c> violation output or hint ever names <c>--add</c>. Each fact batches its
///     assertions to keep the expensive CLI/workspace runs to a minimum.
/// </summary>
[Collection("Serial")]
public sealed class BaselineAddE2ETests
{
    private const string MigrateRule = "data-access/no-inline-sql";
    private const string ContainmentRule = "legacy/billing/containment";
    private const string ForeignRule = "some/other-rule";

    private const string HomeId = "T:MyApp.Web.HomeController";
    private const string InvoiceId = "T:MyApp.Web.InvoiceController";
    private const string DataTableId = "T:System.Data.DataTable";
    private const string DataSetId = "T:System.Data.DataSet";
    private const string BillingCalculatorId = "T:MyApp.Legacy.Billing.BillingCalculator";
    private const string RoundingModeId = "T:MyApp.Legacy.Billing.RoundingMode";
    private const string ForeignSubjectId = "T:MyApp.Other.Widget";

    // A SECOND forbidden System.Data edge on HomeController (DataSet), inserted alongside the stock DataTable
    // one — so the Migrate rule has two current reds and --add grandfathers exactly one of them.
    private const string DataSetMethod =
        "\n    public System.Data.DataSet ExportEverything()\n    {\n        return new System.Data.DataSet();\n    }\n";

    // Exactly ONE new inbound edge into the frozen billing scope (HomeController -> BillingCalculator);
    // .ToString() is object's, so no RoundingMode edge tags along — the containment rule can go fully green.
    private const string DescribeBillingMethod =
        "\n    public string DescribeBilling()\n    {\n        BillingCalculator calculator = new BillingCalculator();\n        return calculator.ToString();\n    }\n";

    private static readonly string[] MigrateBaselineFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];
    private static readonly string[] FreezeBaselineFile = ["arch", "violated-freeze-baseline.json"];
    private static readonly string[] HomeControllerFile = ["MyApp.Web", "HomeController.cs"];

    [Fact]
    public async Task BaselineAdd_MigrateRule_AddsOneAttributedEntryAndBystandersStayRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // A second forbidden edge (DataSet) so the captured Migrate rule carries two reds; --add takes one.
        InsertMember(workspace.PathOf(HomeControllerFile), DataSetMethod);
        // Pre-write a digest-valid file that co-hosts the real section AND a foreign section, to prove --add
        // rides the foreign section through byte-identical.
        string migratePath = workspace.PathOf(MigrateBaselineFile);
        File.WriteAllText(migratePath, ComposeSections(
            (MigrateRule, [BaselineEntry.ForEdge(InvoiceId, DataTableId)]),
            (ForeignRule, [BaselineEntry.ForSubject(ForeignSubjectId)])));
        string beforeText = File.ReadAllText(migratePath);

        // FullName forms (no 'T:') — this pins name resolution to the symbol IDs.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "data-access/no-inline-sql: added 1 grandfathered entry — MyApp.Web.HomeController -> System.Data.DataTable (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: the new entry renders attributed (because last) AND the foreign section is byte-identical.
        string afterText = File.ReadAllText(migratePath);
        Normalize(afterText).ShouldBe(ComposeSections(
            (MigrateRule,
            [
                BaselineEntry.ForEdge(HomeId, DataTableId).WithBecause("INC-1234"),
                BaselineEntry.ForEdge(InvoiceId, DataTableId)
            ]),
            (ForeignRule, [BaselineEntry.ForSubject(ForeignSubjectId)])));

        // A one-line burndown-shaped diff: exactly one new entry line + one bumped digest line added,
        // exactly one old digest line removed (the InvoiceController neighbour stays last, so no comma flip).
        var added = LineSet(afterText).Except(LineSet(beforeText)).ToList();
        var removed = LineSet(beforeText).Except(LineSet(afterText)).ToList();
        added.Count.ShouldBe(2);
        added.Count(line => line.Contains("\"digest\"")).ShouldBe(1);
        added.Count(line => line.Contains(HomeId) && line.Contains("\"because\": \"INC-1234\"")).ShouldBe(1);
        removed.Count.ShouldBe(1);
        removed.Single().ShouldContain("\"digest\"");

        // The bystanders are untouched: the same-rule DataSet red and the other-rule uncaptured containment
        // reds both still fail check.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.Exit.ShouldBe(1);
        check.Out.ShouldContain("MyApp.Web.HomeController references System.Data.DataSet");
        check.Out.ShouldContain("FAIL legacy/billing/containment");
    }

    [Fact]
    public async Task BaselineAdd_FreezeContainment_GrandfathersNewInboundEdge()
    {
        using var workspace = new TempFixtureWorkspace();
        // Capture the uncaptured containment rule first — its two InvoiceController interior edges.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        // Now introduce exactly one new inbound edge into the frozen scope and grandfather it.
        InsertMember(workspace.PathOf(HomeControllerFile), DescribeBillingMethod);

        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ContainmentRule,
            "--source", "MyApp.Web.HomeController", "--target", "MyApp.Legacy.Billing.BillingCalculator",
            "--because", "hotfix INC-42");

        init.Exit.ShouldBe(0);
        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "legacy/billing/containment: added 1 grandfathered entry — MyApp.Web.HomeController -> MyApp.Legacy.Billing.BillingCalculator (because: hotfix INC-42).");

        Normalize(File.ReadAllText(workspace.PathOf(FreezeBaselineFile))).ShouldBe(ComposeSections(
            (ContainmentRule,
            [
                BaselineEntry.ForEdge(HomeId, BillingCalculatorId).WithBecause("hotfix INC-42"),
                BaselineEntry.ForEdge(InvoiceId, BillingCalculatorId),
                BaselineEntry.ForEdge(InvoiceId, RoundingModeId)
            ])));

        // The containment rule is now fully grandfathered — green — while overall check still fails on the
        // untouched HomeController -> DataTable Migrate red.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Exit.ShouldBe(1);
        using JsonDocument document = JsonDocument.Parse(check.Out);
        JsonElement containment = document.RootElement.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == ContainmentRule);
        containment.GetProperty("status").GetString().ShouldBe("passed");
    }

    [Fact]
    public async Task BaselineAdd_PresentEntry_UpdatesAttributionOnly()
    {
        using var workspace = new TempFixtureWorkspace();
        string migratePath = workspace.PathOf(MigrateBaselineFile);

        CliResult first = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "first");
        string afterFirst = File.ReadAllText(migratePath);

        CliResult second = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "second");
        string afterSecond = File.ReadAllText(migratePath);

        first.Out.ShouldContain("added 1 grandfathered entry");
        second.Exit.ShouldBe(0);
        second.Out.ShouldContain("data-access/no-inline-sql: entry already baselined — attribution updated.");
        second.Out.ShouldContain("wrote");
        // No second entry — the count is unchanged and only the attribution (and its digest) moved.
        Normalize(afterSecond).ShouldBe(ComposeSections(
            (MigrateRule,
            [
                BaselineEntry.ForEdge(HomeId, DataTableId).WithBecause("second"),
                BaselineEntry.ForEdge(InvoiceId, DataTableId)
            ])));
        afterSecond.ShouldNotBe(afterFirst);
    }

    [Fact]
    public async Task BaselineAdd_Attribution_RoundTripsInitAndAcceptReductions()
    {
        using var workspace = new TempFixtureWorkspace();
        string migratePath = workspace.PathOf(MigrateBaselineFile);

        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "keep");
        add.Exit.ShouldBe(0);
        byte[] snapshot = File.ReadAllBytes(migratePath);

        // Both entries are still observed, so accept-reductions removes nothing and preserves the attribution.
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.Exit.ShouldBe(0);
        File.ReadAllBytes(migratePath).ShouldBe(snapshot);

        // The Migrate rule is already captured, so --init leaves the attributed entry (and digest) byte-identical.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        File.ReadAllBytes(migratePath).ShouldBe(snapshot);
    }

    [Fact]
    public async Task BaselineAdd_Refusals_ExitTwoWithPinnedMessages()
    {
        using var workspace = new TempFixtureWorkspace();

        // Unknown rule: refused before any matching.
        CliResult unknown = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", "nope/nothing", "--because", "b", "--subject", "X");
        // Non-ratcheted (an Enforce rule carries no baseline).
        CliResult nonRatcheted = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", "layering/domain-independent", "--because", "b", "--subject", "X");
        // Uncaptured: the containment baseline file is absent in the stock fixture.
        CliResult uncaptured = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ContainmentRule,
            "--source", "MyApp.Web.InvoiceController", "--target", "MyApp.Legacy.Billing.BillingCalculator", "--because", "b");
        // No match: DataSet is not a current violation of the Migrate rule in the unmutated fixture.
        CliResult noMatch = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataSet", "--because", "b");

        unknown.Exit.ShouldBe(2);
        unknown.Err.ShouldContain("rule 'nope/nothing' is not in the spec.");

        nonRatcheted.Exit.ShouldBe(2);
        nonRatcheted.Err.ShouldContain(
            "rule 'layering/domain-independent' is not ratcheted — only Migrate and Freeze containment rules carry baselines.");

        uncaptured.Exit.ShouldBe(2);
        uncaptured.Err.ShouldContain(
            "no baseline section for 'legacy/billing/containment' — run 'loadbearing baseline --init' first.");

        noMatch.Exit.ShouldBe(2);
        noMatch.Err.ShouldContain(
            "no current violation of 'data-access/no-inline-sql' matches --source 'MyApp.Web.HomeController' --target 'System.Data.DataSet'");
        noMatch.Err.ShouldContain("the baseline records observed reality");
        noMatch.Err.ShouldContain("MyApp.Web.HomeController -> System.Data.DataTable");
    }

    [Fact]
    public async Task CheckAndStatus_ViolatedFixture_NeverHintAdd()
    {
        // Read-only against the shared fixture — check/status never mutate baselines. The valve is
        // deliberately invisible to the reporting surfaces: a violation is grandfathered only by a human
        // running baseline --add, never suggested by the tool.
        CliResult check = await CliRunner.InvokeAsync("check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);
        CliResult checkJson = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll, "--json");
        CliResult status = await CliRunner.InvokeAsync("status", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        check.Out.ShouldNotContain("--add");
        check.Err.ShouldNotContain("--add");
        checkJson.Out.ShouldNotContain("--add");
        checkJson.Err.ShouldNotContain("--add");
        status.Out.ShouldNotContain("--add");
        status.Err.ShouldNotContain("--add");
    }

    // Inserts a member into the (single) class in a source file, just before its closing brace — line-ending
    // agnostic (it only anchors on the final '}'), so a CRLF or LF fixture checkout both work.
    private static void InsertMember(string filePath, string member)
    {
        string original = File.ReadAllText(filePath);
        int lastBrace = original.LastIndexOf('}');
        File.WriteAllText(filePath, original[..lastBrace] + member + original[lastBrace..]);
    }

    private static string ComposeSections(params (string RuleId, BaselineEntry[] Entries)[] sections)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal);
        foreach ((string ruleId, var entries) in sections) rules[ruleId] = entries;
        return BaselineFormat.ComposeFile(rules);
    }

    private static HashSet<string> LineSet(string text)
    {
        return Normalize(text)
            .Split('\n')
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}
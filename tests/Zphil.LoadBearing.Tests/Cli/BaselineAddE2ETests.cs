using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline --add</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />, one per fact) — the ratchet's escape valve.
///     Pins the whole valve: a Migrate <c>--add</c> appends exactly one attributed entry as a one-line
///     diff while same-rule and other-rule bystanders stay red; a Freeze-containment <c>--add</c>
///     grandfathers a new inbound edge and turns the rule green; a present entry only has its attribution
///     updated; the attribution survives an <c>--init</c>/<c>--accept-reductions</c> round-trip
///     byte-for-byte; the four refusals exit 2 with their pinned messages; and — by design —
///     no <c>check</c>/<c>status</c> violation output or hint ever names <c>--add</c>. Each fact batches its
///     assertions to keep the expensive CLI/workspace runs to a minimum.
/// </summary>
[Collection("Serial")]
public sealed class BaselineAddE2ETests
{
    private const string MigrateRule = "data-access/no-inline-sql";
    private const string ContainmentRule = "legacy/billing/containment";
    private const string ClockRule = "time/inject-clock";
    private const string AsyncRule = "naming/async-suffix";
    private const string ConstructionRule = "di/handlers-via-registry";
    private const string CaptiveRule = "di/no-captive-dependencies";
    private const string ForeignRule = "some/other-rule";

    private const string NowMemberId = "P:System.DateTime.Now";
    private const string UtcNowMemberId = "P:System.DateTime.UtcNow";

    private const string SaveMemberId = "M:MyApp.Web.HomeController.Save";
    private const string LoadMemberId = "M:MyApp.Web.HomeController.Load";

    private const string HomeId = "T:MyApp.Web.HomeController";
    private const string InvoiceId = "T:MyApp.Web.InvoiceController";
    private const string DataTableId = "T:System.Data.DataTable";
    private const string BillingCalculatorId = "T:MyApp.Legacy.Billing.BillingCalculator";
    private const string RoundingModeId = "T:MyApp.Legacy.Billing.RoundingMode";
    private const string ForeignSubjectId = "T:MyApp.Other.Widget";

    private const string InvoiceServiceId = "T:MyApp.Web.InvoiceService";
    private const string InvoiceCreatedHandlerId = "T:MyApp.Web.InvoiceCreatedHandler";

    private const string ReportSchedulerId = "T:MyApp.Web.ReportScheduler";
    private const string OrderFeedInterfaceId = "T:MyApp.Web.IOrderFeed";

    private const string CatchRule = "exceptions/no-general-catch";
    private const string ThrowRule = "exceptions/domain-throws-domain";
    private const string ReportEndpointId = "T:MyApp.Web.ReportEndpoint";
    private const string SystemExceptionId = "T:System.Exception";

    // A SECOND forbidden System.Data edge on HomeController (DataSet), inserted alongside the stock DataTable
    // one — so the Migrate rule has two current reds and --add grandfathers exactly one of them.
    private const string DataSetMethod =
        "\n    public System.Data.DataSet ExportEverything()\n    {\n        return new System.Data.DataSet();\n    }\n";

    // Exactly ONE new inbound edge into the frozen billing scope (HomeController -> BillingCalculator);
    // .ToString() is object's, so no RoundingMode edge tags along — the containment rule can go fully green.
    private const string DescribeBillingMethod =
        "\n    public string DescribeBilling()\n    {\n        BillingCalculator calculator = new BillingCalculator();\n        return calculator.ToString();\n    }\n";

    // A SECOND construction of the handler on a NON-registry type (HomeController) — a distinct (source,
    // constructed) identity from InvoiceService's stock red, so --add grandfathers one and this stays red.
    private const string ConstructHandlerMethod =
        "\n    public IHandler<InvoiceCreated> BuildHandler()\n    {\n        return new InvoiceCreatedHandler();\n    }\n";

    private static readonly string[] MigrateBaselineFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];
    private static readonly string[] FreezeBaselineFile = ["arch", "violated-freeze-baseline.json"];
    private static readonly string[] ClockBaselineFile = ["arch", "baselines", "time", "inject-clock.json"];
    private static readonly string[] AsyncBaselineFile = ["arch", "baselines", "naming", "async-suffix.json"];
    private static readonly string[] ConstructionBaselineFile = ["arch", "baselines", "di", "handlers-via-registry.json"];
    private static readonly string[] CaptiveBaselineFile = ["arch", "baselines", "di", "no-captive-dependencies.json"];
    private static readonly string[] CatchBaselineFile = ["arch", "baselines", "exceptions", "no-general-catch.json"];
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
    public async Task BaselineAdd_MemberRule_GrandfathersOneClockReadAndBystanderUtcNowStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the member-level Migrate rule is uncaptured, so both of HomeController's ambient-clock
        // reads (GRAMMAR §4.5) are current memberUse violations.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement redRule = RuleElement(red.Out, ClockRule);
        redRule.GetProperty("status").GetString().ShouldBe("failed");
        redRule.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("targetMember").GetString())
            .ShouldBe([NowMemberId, UtcNowMemberId], true);

        // Capture: --init grandfathers both member identities (T: source x P: member DocId entries), and the
        // re-check sees the rule green with both reads riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("time/inject-clock: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement capturedRule = RuleElement(captured.Out, ClockRule);
        capturedRule.GetProperty("status").GetString().ShouldBe("passed");
        capturedRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(2);

        // Un-capture the pair (composer as arrangement, the Migrate fact's idiom): a digest-valid EMPTY
        // section turns both reads red again on a captured rule — the state the valve exists for.
        string clockPath = workspace.PathOf(ClockBaselineFile);
        File.WriteAllText(clockPath, ComposeSections((ClockRule, [])));
        string beforeText = File.ReadAllText(clockPath);

        // The valve, member flavor: a full-name --target (no P: prefix) resolves the member violation.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ClockRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.DateTime.Now", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "time/inject-clock: added 1 grandfathered entry — MyApp.Web.HomeController -> System.DateTime.Now (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry line keying the P: member DocId, plus the digest change.
        string afterText = File.ReadAllText(clockPath);
        Normalize(afterText).ShouldBe(ComposeSections(
            (ClockRule, [BaselineEntry.ForEdge(HomeId, NowMemberId).WithBecause("INC-1234")])));
        afterText.ShouldNotBe(beforeText);
        LineSet(afterText).Count(line => line.Contains("\"source\":")).ShouldBe(1);

        // The bystander pin: the OTHER clock read (UtcNow) is still red; only Now is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement clockRule = RuleElement(check.Out, ClockRule);
        clockRule.GetProperty("status").GetString().ShouldBe("failed");
        clockRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);
        var bystanders = clockRule.GetProperty("violations").EnumerateArray().ToList();
        JsonElement bystander = bystanders.ShouldHaveSingleItem();
        bystander.GetProperty("kind").GetString().ShouldBe("memberUse");
        bystander.GetProperty("targetMember").GetString().ShouldBe(UtcNowMemberId);

        // The attribution round-trips: --accept-reductions keeps the still-observed Now entry (refusing the
        // UtcNow growth) and a second --init leaves the captured section be — byte-identical both ways.
        byte[] snapshot = File.ReadAllBytes(clockPath);
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.Exit.ShouldBe(0);
        File.ReadAllBytes(clockPath).ShouldBe(snapshot);
        CliResult reinit = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        reinit.Exit.ShouldBe(0);
        File.ReadAllBytes(clockPath).ShouldBe(snapshot);
    }

    [Fact]
    public async Task BaselineAdd_MemberSubjectRule_GrandfathersOneTaskMethodAndBystanderLoadStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the member-subject Migrate rule naming/async-suffix is uncaptured, so both of
        // HomeController's unsuffixed Task-returning methods (Save, Load — GRAMMAR §4.6) are current
        // memberShape violations, each keyed by the member's own DocId in the subjectMember field.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement redRule = RuleElement(red.Out, AsyncRule);
        redRule.GetProperty("status").GetString().ShouldBe("failed");
        redRule.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("subjectMember").GetString())
            .ShouldBe([SaveMemberId, LoadMemberId], true);

        // Capture: --init grandfathers both member identities (M: member DocId, ForSubject entries), and the
        // re-check sees the rule green with both methods riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("naming/async-suffix: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement capturedRule = RuleElement(captured.Out, AsyncRule);
        capturedRule.GetProperty("status").GetString().ShouldBe("passed");
        capturedRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(2);

        // Un-capture the pair (composer as arrangement, the Migrate/clock fact's idiom): a digest-valid EMPTY
        // section turns both methods red again on a captured rule — the state the valve exists for.
        string asyncPath = workspace.PathOf(AsyncBaselineFile);
        File.WriteAllText(asyncPath, ComposeSections((AsyncRule, [])));

        // The valve, member-subject flavor: a full-name --subject (no M: prefix, no parens) resolves the
        // member-shape violation, and the echo renders Save() WITH parens exactly as 'loadbearing check' does.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", AsyncRule,
            "--subject", "MyApp.Web.HomeController.Save", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "naming/async-suffix: added 1 grandfathered entry — MyApp.Web.HomeController.Save() (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the M: member DocId via ForSubject, plus the digest.
        Normalize(File.ReadAllText(asyncPath)).ShouldBe(ComposeSections(
            (AsyncRule, [BaselineEntry.ForSubject(SaveMemberId).WithBecause("INC-1234")])));
        LineSet(File.ReadAllText(asyncPath)).Count(line => line.Contains("\"subject\":")).ShouldBe(1);

        // The bystander pin: the OTHER Task method (Load) is still red; only Save is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement asyncRule = RuleElement(check.Out, AsyncRule);
        asyncRule.GetProperty("status").GetString().ShouldBe("failed");
        asyncRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);
        JsonElement bystander = asyncRule.GetProperty("violations").EnumerateArray().ToList().ShouldHaveSingleItem();
        bystander.GetProperty("kind").GetString().ShouldBe("memberShape");
        bystander.GetProperty("subjectMember").GetString().ShouldBe(LoadMemberId);

        // The attribution round-trips: --accept-reductions keeps the still-observed Save entry (refusing the
        // Load growth) and a second --init leaves the captured section be — byte-identical both ways.
        byte[] snapshot = File.ReadAllBytes(asyncPath);
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.Exit.ShouldBe(0);
        File.ReadAllBytes(asyncPath).ShouldBe(snapshot);
        CliResult reinit = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        reinit.Exit.ShouldBe(0);
        File.ReadAllBytes(asyncPath).ShouldBe(snapshot);
    }

    [Fact]
    public async Task BaselineAdd_ConstructionRule_GrandfathersFlagshipEdgeAndBystanderStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // A SECOND construction red (HomeController news up the handler too), inserted BEFORE --init so both
        // construction reds are captured — a distinct (source, constructed) identity from InvoiceService's stock red.
        InsertMember(workspace.PathOf(HomeControllerFile), ConstructHandlerMethod);

        // Capture both construction reds (this also mints the arch/baselines/di/ directory), then un-capture the
        // pair (composer as arrangement, the member facts' idiom): a digest-valid EMPTY section turns both reds
        // live again on a captured rule — the state the valve exists for.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("di/handlers-via-registry: captured 2 grandfathered violations.");
        string diPath = workspace.PathOf(ConstructionBaselineFile);
        File.WriteAllText(diPath, ComposeSections((ConstructionRule, [])));

        // The valve, construction flavor: full-name --source/--target (no T: prefix) resolve the (source,
        // constructed) type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ConstructionRule,
            "--source", "MyApp.Web.InvoiceService", "--target", "MyApp.Web.InvoiceCreatedHandler", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "di/handlers-via-registry: added 1 grandfathered entry — MyApp.Web.InvoiceService -> MyApp.Web.InvoiceCreatedHandler (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, constructed) type pair via ForEdge.
        Normalize(File.ReadAllText(diPath)).ShouldBe(ComposeSections(
            (ConstructionRule, [BaselineEntry.ForEdge(InvoiceServiceId, InvoiceCreatedHandlerId).WithBecause("INC-1234")])));
        LineSet(File.ReadAllText(diPath)).Count(line => line.Contains("\"source\":")).ShouldBe(1);

        // The bystander pin: the OTHER construction (HomeController) is still red; only InvoiceService is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Exit.ShouldBe(1);
        JsonElement diRule = RuleElement(check.Out, ConstructionRule);
        diRule.GetProperty("status").GetString().ShouldBe("failed");
        diRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);
        JsonElement bystander = diRule.GetProperty("violations").EnumerateArray().ToList().ShouldHaveSingleItem();
        bystander.GetProperty("kind").GetString().ShouldBe("construction");
        bystander.GetProperty("source").GetString().ShouldBe("MyApp.Web.HomeController");
        bystander.GetProperty("target").GetString().ShouldBe("MyApp.Web.InvoiceCreatedHandler");
    }

    [Fact]
    public async Task BaselineAdd_InjectionRule_GrandfathersOneCaptiveEdgeAndBystanderFormatterStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the injection Migrate rule di/no-captive-dependencies is uncaptured, so both of the
        // singleton ReportScheduler's captive edges (GRAMMAR §4.7) are current injection violations — a scoped
        // IOrderFeed and a transient IOrderFormatter, each keyed by the (source, injected) type pair.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement redRule = RuleElement(red.Out, CaptiveRule);
        redRule.GetProperty("status").GetString().ShouldBe("failed");
        redRule.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("target").GetString())
            .ShouldBe(["MyApp.Web.IOrderFeed", "MyApp.Web.IOrderFormatter"], true);

        // Capture: --init grandfathers both injection identities (T: source × T: injected ForEdge entries), and
        // the re-check sees the rule green with both captive edges riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("di/no-captive-dependencies: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement capturedRule = RuleElement(captured.Out, CaptiveRule);
        capturedRule.GetProperty("status").GetString().ShouldBe("passed");
        capturedRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(2);

        // Un-capture the pair (composer as arrangement, the member/construction facts' idiom): a digest-valid
        // EMPTY section turns both captive edges red again on a captured rule — the state the valve exists for.
        string captivePath = workspace.PathOf(CaptiveBaselineFile);
        File.WriteAllText(captivePath, ComposeSections((CaptiveRule, [])));

        // The valve, injection flavor: full-name --source/--target (no T: prefix) resolve the (source, injected)
        // type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", CaptiveRule,
            "--source", "MyApp.Web.ReportScheduler", "--target", "MyApp.Web.IOrderFeed", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "di/no-captive-dependencies: added 1 grandfathered entry — MyApp.Web.ReportScheduler -> MyApp.Web.IOrderFeed (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, injected) type pair via ForEdge.
        Normalize(File.ReadAllText(captivePath)).ShouldBe(ComposeSections(
            (CaptiveRule, [BaselineEntry.ForEdge(ReportSchedulerId, OrderFeedInterfaceId).WithBecause("INC-1234")])));
        LineSet(File.ReadAllText(captivePath)).Count(line => line.Contains("\"source\":")).ShouldBe(1);

        // The bystander pin: the OTHER captive edge (IOrderFormatter) is still red; only IOrderFeed is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Exit.ShouldBe(1);
        JsonElement captiveRule = RuleElement(check.Out, CaptiveRule);
        captiveRule.GetProperty("status").GetString().ShouldBe("failed");
        captiveRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);
        JsonElement bystander = captiveRule.GetProperty("violations").EnumerateArray().ToList().ShouldHaveSingleItem();
        bystander.GetProperty("kind").GetString().ShouldBe("injection");
        bystander.GetProperty("source").GetString().ShouldBe("MyApp.Web.ReportScheduler");
        bystander.GetProperty("target").GetString().ShouldBe("MyApp.Web.IOrderFormatter");

        // The attribution round-trips: --accept-reductions keeps the still-observed IOrderFeed entry (refusing
        // the IOrderFormatter growth) and a second --init leaves the captured section be — byte-identical both ways.
        byte[] snapshot = File.ReadAllBytes(captivePath);
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.Exit.ShouldBe(0);
        File.ReadAllBytes(captivePath).ShouldBe(snapshot);
        CliResult reinit = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        reinit.Exit.ShouldBe(0);
        File.ReadAllBytes(captivePath).ShouldBe(snapshot);
    }

    [Fact]
    public async Task BaselineAdd_CatchRule_GrandfathersOneSwallowAndThrowBystanderStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the catch Migrate rule exceptions/no-general-catch is uncaptured, so ReportEndpoint's
        // blanket catch (GRAMMAR §4.8) is a current catch violation keyed by the (source, caught) type pair.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement redRule = RuleElement(red.Out, CatchRule);
        redRule.GetProperty("status").GetString().ShouldBe("failed");
        redRule.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("target").GetString())
            .ShouldBe(["System.Exception"], true);

        // Capture: --init grandfathers the catch identity (T: source × T: caught ForEdge entry, minting the
        // arch/baselines/exceptions/ directory), and the re-check sees the rule green with the swallow baselined.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.Exit.ShouldBe(0);
        init.Out.ShouldContain("exceptions/no-general-catch: captured 1 grandfathered");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        JsonElement capturedRule = RuleElement(captured.Out, CatchRule);
        capturedRule.GetProperty("status").GetString().ShouldBe("passed");
        capturedRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);

        // Un-capture (composer as arrangement, the member/construction/injection facts' idiom): a digest-valid
        // EMPTY section turns the swallow red again on a captured rule — the state the valve exists for.
        string catchPath = workspace.PathOf(CatchBaselineFile);
        File.WriteAllText(catchPath, ComposeSections((CatchRule, [])));

        // The valve, catch flavor: full-name --source/--target (no T: prefix) resolve the (source, caught)
        // type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", CatchRule,
            "--source", "MyApp.Web.ReportEndpoint", "--target", "System.Exception", "--because", "INC-1234");

        add.Exit.ShouldBe(0);
        add.Out.ShouldContain(
            "exceptions/no-general-catch: added 1 grandfathered entry — MyApp.Web.ReportEndpoint -> System.Exception (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, caught) type pair via ForEdge.
        Normalize(File.ReadAllText(catchPath)).ShouldBe(ComposeSections(
            (CatchRule, [BaselineEntry.ForEdge(ReportEndpointId, SystemExceptionId).WithBecause("INC-1234")])));
        LineSet(File.ReadAllText(catchPath)).Count(line => line.Contains("\"source\":")).ShouldBe(1);

        // The added swallow now passes; the cross-rule bystander — the strict Enforce throw rule's BCL throw,
        // which is never ratcheted and so can never be grandfathered — stays red.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Exit.ShouldBe(1);
        JsonElement catchRule = RuleElement(check.Out, CatchRule);
        catchRule.GetProperty("status").GetString().ShouldBe("passed");
        catchRule.GetProperty("baseline").GetProperty("grandfathered").GetInt32().ShouldBe(1);
        JsonElement throwRule = RuleElement(check.Out, ThrowRule);
        throwRule.GetProperty("status").GetString().ShouldBe("failed");
        JsonElement bystander = throwRule.GetProperty("violations").EnumerateArray().ToList().ShouldHaveSingleItem();
        bystander.GetProperty("kind").GetString().ShouldBe("throw");
        bystander.GetProperty("source").GetString().ShouldBe("MyApp.Domain.OrderApproval");
        bystander.GetProperty("target").GetString().ShouldBe("System.InvalidOperationException");

        // The attribution round-trips: --accept-reductions keeps the still-observed swallow entry and a second
        // --init leaves the captured section be — byte-identical both ways.
        byte[] snapshot = File.ReadAllBytes(catchPath);
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.Exit.ShouldBe(0);
        File.ReadAllBytes(catchPath).ShouldBe(snapshot);
        CliResult catchReinit = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        catchReinit.Exit.ShouldBe(0);
        File.ReadAllBytes(catchPath).ShouldBe(snapshot);
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

    // One rule's object from a check --json document, cloned so it outlives the parse.
    private static JsonElement RuleElement(string checkJson, string ruleId)
    {
        using JsonDocument document = JsonDocument.Parse(checkJson);
        return document.RootElement.GetProperty("rules").EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == ruleId)
            .Clone();
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
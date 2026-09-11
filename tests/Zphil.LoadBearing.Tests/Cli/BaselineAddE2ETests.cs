using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>baseline --add</c> against a private, restored copy of the MyApp fixture
///     (<see cref="TempFixtureWorkspace" />: one copy leased per test <em>class</em>, keyed on
///     <c>[CallerFilePath]</c> and reset to pristine between facts) — the ratchet's escape valve.
///     Pins the whole valve: a Migrate <c>--add</c> appends exactly one attributed entry as a one-line
///     diff while same-rule and other-rule bystanders stay red; a Quarantine-containment <c>--add</c>
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
    private const string ExposeRule = "api/return-dtos";
    private const string ReportEndpointId = "T:MyApp.Web.ReportEndpoint";
    private const string SystemExceptionId = "T:System.Exception";

    // A SECOND forbidden System.Data edge on HomeController (DataSet), inserted alongside the stock DataTable
    // one — so the Migrate rule has two current reds and --add grandfathers exactly one of them.
    private const string DataSetMethod =
        "\n    public System.Data.DataSet ExportEverything()\n    {\n        return new System.Data.DataSet();\n    }\n";

    // Exactly ONE new inbound edge into the quarantined billing scope (HomeController -> BillingCalculator);
    // .ToString() is object's, so no RoundingMode edge tags along — the containment rule can go fully green.
    private const string DescribeBillingMethod =
        "\n    public string DescribeBilling()\n    {\n        BillingCalculator calculator = new BillingCalculator();\n        return calculator.ToString();\n    }\n";

    // A SECOND construction of the handler on a NON-registry type (HomeController) — a distinct (source,
    // constructed) identity from InvoiceService's stock red, so --add grandfathers one and this stays red.
    private const string ConstructHandlerMethod =
        "\n    public IHandler<InvoiceCreated> BuildHandler()\n    {\n        return new InvoiceCreatedHandler();\n    }\n";

    private static readonly string[] MigrateBaselineFile = ["arch", "baselines", "data-access", "no-inline-sql.json"];
    private static readonly string[] QuarantineBaselineFile = ["arch", "violated-quarantine-baseline.json"];
    private static readonly string[] ClockBaselineFile = ["arch", "baselines", "time", "inject-clock.json"];
    private static readonly string[] AsyncBaselineFile = ["arch", "baselines", "naming", "async-suffix.json"];
    private static readonly string[] ConstructionBaselineFile = ["arch", "baselines", "di", "handlers-via-registry.json"];
    private static readonly string[] CaptiveBaselineFile = ["arch", "baselines", "di", "no-captive-dependencies.json"];
    private static readonly string[] CatchBaselineFile = ["arch", "baselines", "exceptions", "no-general-catch.json"];
    private static readonly string[] ExposeBaselineFile = ["arch", "baselines", "api", "return-dtos.json"];
    private static readonly string[] HomeControllerFile = ["MyApp.Web", "HomeController.cs"];

    [Fact]
    public async Task BaselineAdd_MigrateRule_AddsOneAttributedEntryAndBystandersStayRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // A second forbidden edge (DataSet) so the captured Migrate rule carries two reds; --add takes one.
        FixtureEdits.InsertMember(workspace.PathOf(HomeControllerFile), DataSetMethod);
        // Pre-write a valid file that co-hosts the real section AND a foreign section, to prove --add
        // rides the foreign section through byte-identical.
        string migratePath = workspace.PathOf(MigrateBaselineFile);
        File.WriteAllText(migratePath, BaselineComposer.Compose(
            (MigrateRule, [BaselineEntry.ForEdge(InvoiceId, DataTableId)]),
            (ForeignRule, [BaselineEntry.ForSubject(ForeignSubjectId)])));
        string beforeText = File.ReadAllText(migratePath);

        // FullName forms (no 'T:') — this pins name resolution to the symbol IDs.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", MigrateRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "INC-1234");

        add.ShouldSucceed(
            "data-access/no-inline-sql: added 1 grandfathered entry — MyApp.Web.HomeController -> System.Data.DataTable (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: the new entry renders measured then attributed (siteCount, then because last) AND
        // the foreign section is byte-identical. HomeController declares the DataTable on two lines, so the valve
        // records two sites; the pre-written InvoiceController neighbour is not the entry being added and stays
        // uncounted, which is the mixed shape a partially upgraded file has.
        string afterText = File.ReadAllText(migratePath);
        afterText.NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (MigrateRule,
                [
                    BaselineEntry.ForEdge(HomeId, DataTableId)
                        .WithSiteCount(2)
                        .WithBecause("INC-1234"),
                    BaselineEntry.ForEdge(InvoiceId, DataTableId)
                ]),
                (ForeignRule, [BaselineEntry.ForSubject(ForeignSubjectId)])));

        // A one-line burndown-shaped diff, and now literally one line: the new entry, with nothing removed.
        // There is no whole-file digest to bump, and the InvoiceController neighbour stays last, so no comma
        // flips and its own seal is untouched. That is what lets two branches grow or shrink one file at once.
        List<string> added = LineSet(afterText)
            .Except(LineSet(beforeText))
            .ToList();
        List<string> removed = LineSet(beforeText)
            .Except(LineSet(afterText))
            .ToList();
        string addedLine = added.ShouldHaveSingleItem();
        addedLine.ShouldContain(HomeId);
        addedLine.ShouldContain("\"because\": \"INC-1234\"");
        removed.ShouldBeEmpty();

        // The bystanders are untouched: the same-rule DataSet red and the other-rule uncaptured containment
        // reds both still fail check.
        CliResult check = await CliRunner.InvokeAsync("check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll);
        check.ShouldReportViolations("MyApp.Web.HomeController references System.Data.DataSet");
        check.Out.ShouldContain("FAIL legacy/billing/containment");
    }

    [Fact]
    public async Task BaselineAdd_QuarantineContainment_GrandfathersNewInboundEdge()
    {
        using var workspace = new TempFixtureWorkspace();
        // Capture the uncaptured containment rule first — its two InvoiceController interior edges.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        // Now introduce exactly one new inbound edge into the quarantined scope and grandfather it.
        FixtureEdits.InsertMember(workspace.PathOf(HomeControllerFile), DescribeBillingMethod);

        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ContainmentRule,
            "--source", "MyApp.Web.HomeController", "--target", "MyApp.Legacy.Billing.BillingCalculator",
            "--because", "hotfix INC-42");

        init.ShouldSucceed();
        add.ShouldSucceed(
            "legacy/billing/containment: added 1 grandfathered entry — MyApp.Web.HomeController -> MyApp.Legacy.Billing.BillingCalculator (because: hotfix INC-42).");

        File.ReadAllText(workspace.PathOf(QuarantineBaselineFile))
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (ContainmentRule,
                [
                    BaselineEntry.ForEdge(HomeId, BillingCalculatorId)
                        .WithSiteCount(1)
                        .WithBecause("hotfix INC-42"),
                    BaselineEntry.ForEdge(InvoiceId, BillingCalculatorId)
                        .WithSiteCount(2),
                    BaselineEntry.ForEdge(InvoiceId, RoundingModeId)
                        .WithSiteCount(1)
                ])));

        // The containment rule is now fully grandfathered — green — while overall check still fails on the
        // untouched HomeController -> DataTable Migrate red.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.ShouldReportViolations();
        check.Out.ShouldHavePassed(ContainmentRule);
    }

    [Fact]
    public async Task BaselineAdd_MemberRule_GrandfathersOneClockReadAndBystanderUtcNowStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the member-level Migrate rule is uncaptured, so both of HomeController's ambient-clock
        // reads (GRAMMAR §4.5) are current memberUse violations.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        red.Out.ShouldHaveFailedWith(ClockRule, "targetMember", [NowMemberId, UtcNowMemberId]);

        // Capture: --init grandfathers both member identities (T: source x P: member DocId entries), and the
        // re-check sees the rule green with both reads riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("time/inject-clock: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        captured.Out.ShouldHaveCaptured(ClockRule, 2);

        // Un-capture the pair (composer as arrangement, the Migrate fact's idiom): a sealed, valid EMPTY
        // section turns both reads red again on a captured rule — the state the valve exists for.
        string clockPath = workspace.PathOf(ClockBaselineFile);
        File.WriteAllText(clockPath, BaselineComposer.Compose((ClockRule, [])));
        string beforeText = File.ReadAllText(clockPath);

        // The valve, member flavor: a full-name --target (no P: prefix) resolves the member violation.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ClockRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.DateTime.Now", "--because", "INC-1234");

        add.ShouldSucceed(
            "time/inject-clock: added 1 grandfathered entry — MyApp.Web.HomeController -> System.DateTime.Now (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry line keying the P: member DocId, and nothing else moves.
        string afterText = File.ReadAllText(clockPath);
        afterText.NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (ClockRule, [
                    BaselineEntry.ForEdge(HomeId, NowMemberId)
                        .WithSiteCount(1)
                        .WithBecause("INC-1234")
                ])));
        afterText.ShouldNotBe(beforeText);
        LineSet(afterText)
            .ShouldContain(line => line.Contains("\"source\":"), expectedCount: 1);

        // The bystander pin: the OTHER clock read (UtcNow) is still red; only Now is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Out
            .ShouldHaveGrandfathered(ClockRule, 1)
            .ShouldHaveSingleViolationOnMember(
                ClockRule, kind: "memberUse", slot: "targetMember", member: UtcNowMemberId);

        // The attribution round-trips: --accept-reductions keeps the still-observed Now entry (refusing the
        // UtcNow growth) and a second --init leaves the captured section be — byte-identical both ways.
        await ShouldRoundTripByteIdenticalAsync(workspace, clockPath);
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
        red.Out.ShouldHaveFailedWith(AsyncRule, "subjectMember", [SaveMemberId, LoadMemberId]);

        // Capture: --init grandfathers both member identities (M: member DocId, ForSubject entries), and the
        // re-check sees the rule green with both methods riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("naming/async-suffix: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        captured.Out.ShouldHaveCaptured(AsyncRule, 2);

        // Un-capture the pair (composer as arrangement, the Migrate/clock fact's idiom): a sealed, valid EMPTY
        // section turns both methods red again on a captured rule — the state the valve exists for.
        string asyncPath = workspace.PathOf(AsyncBaselineFile);
        File.WriteAllText(asyncPath, BaselineComposer.Compose((AsyncRule, [])));

        // The valve, member-subject flavor: a full-name --subject (no M: prefix, no parens) resolves the
        // member-shape violation, and the echo renders Save() WITH parens exactly as 'loadbearing check' does.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", AsyncRule,
            "--subject", "MyApp.Web.HomeController.Save", "--because", "INC-1234");

        add.ShouldSucceed(
            "naming/async-suffix: added 1 grandfathered entry — MyApp.Web.HomeController.Save() (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the M: member DocId via ForSubject, and nothing else.
        File.ReadAllText(asyncPath)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (AsyncRule, [
                    BaselineEntry.ForSubject(SaveMemberId)
                        .WithBecause("INC-1234")
                ])));
        LineSet(File.ReadAllText(asyncPath))
            .ShouldContain(line => line.Contains("\"subject\":"), expectedCount: 1);

        // The bystander pin: the OTHER Task method (Load) is still red; only Save is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.Out
            .ShouldHaveGrandfathered(AsyncRule, 1)
            .ShouldHaveSingleViolationOnMember(
                AsyncRule, kind: "memberShape", slot: "subjectMember", member: LoadMemberId);

        // The attribution round-trips: --accept-reductions keeps the still-observed Save entry (refusing the
        // Load growth) and a second --init leaves the captured section be — byte-identical both ways.
        await ShouldRoundTripByteIdenticalAsync(workspace, asyncPath);
    }

    [Fact]
    public async Task BaselineAdd_ConstructionRule_GrandfathersFlagshipEdgeAndBystanderStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();
        // A SECOND construction red (HomeController news up the handler too), inserted BEFORE --init so both
        // construction reds are captured — a distinct (source, constructed) identity from InvoiceService's stock red.
        FixtureEdits.InsertMember(workspace.PathOf(HomeControllerFile), ConstructHandlerMethod);

        // Capture both construction reds (this also mints the arch/baselines/di/ directory), then un-capture the
        // pair (composer as arrangement, the member facts' idiom): a sealed, valid EMPTY section turns both reds
        // live again on a captured rule — the state the valve exists for.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("di/handlers-via-registry: captured 2 grandfathered violations.");
        string diPath = workspace.PathOf(ConstructionBaselineFile);
        File.WriteAllText(diPath, BaselineComposer.Compose((ConstructionRule, [])));

        // The valve, construction flavor: full-name --source/--target (no T: prefix) resolve the (source,
        // constructed) type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ConstructionRule,
            "--source", "MyApp.Web.InvoiceService", "--target", "MyApp.Web.InvoiceCreatedHandler", "--because", "INC-1234");

        add.ShouldSucceed(
            "di/handlers-via-registry: added 1 grandfathered entry — MyApp.Web.InvoiceService -> MyApp.Web.InvoiceCreatedHandler (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, constructed) type pair via ForEdge.
        File.ReadAllText(diPath)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (ConstructionRule, [
                    BaselineEntry.ForEdge(InvoiceServiceId, InvoiceCreatedHandlerId)
                        .WithSiteCount(1)
                        .WithBecause("INC-1234")
                ])));
        LineSet(File.ReadAllText(diPath))
            .ShouldContain(line => line.Contains("\"source\":"), expectedCount: 1);

        // The bystander pin: the OTHER construction (HomeController) is still red; only InvoiceService is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.ShouldReportViolations();
        check.Out
            .ShouldHaveGrandfathered(ConstructionRule, 1)
            .ShouldHaveSingleViolationOnEdge(
                ConstructionRule, kind: "construction",
                source: "MyApp.Web.HomeController", target: "MyApp.Web.InvoiceCreatedHandler");
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
        red.Out.ShouldHaveFailedWith(CaptiveRule, "target", ["MyApp.Web.IOrderFeed", "MyApp.Web.IOrderFormatter"]);

        // Capture: --init grandfathers both injection identities (T: source × T: injected ForEdge entries), and
        // the re-check sees the rule green with both captive edges riding the baseline.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("di/no-captive-dependencies: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        captured.Out.ShouldHaveCaptured(CaptiveRule, 2);

        // Un-capture the pair (composer as arrangement, the member/construction facts' idiom): a sealed, valid
        // EMPTY section turns both captive edges red again on a captured rule — the state the valve exists for.
        string captivePath = workspace.PathOf(CaptiveBaselineFile);
        File.WriteAllText(captivePath, BaselineComposer.Compose((CaptiveRule, [])));

        // The valve, injection flavor: full-name --source/--target (no T: prefix) resolve the (source, injected)
        // type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", CaptiveRule,
            "--source", "MyApp.Web.ReportScheduler", "--target", "MyApp.Web.IOrderFeed", "--because", "INC-1234");

        add.ShouldSucceed(
            "di/no-captive-dependencies: added 1 grandfathered entry — MyApp.Web.ReportScheduler -> MyApp.Web.IOrderFeed (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, injected) type pair via ForEdge.
        File.ReadAllText(captivePath)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (CaptiveRule, [
                    BaselineEntry.ForEdge(ReportSchedulerId, OrderFeedInterfaceId)
                        .WithSiteCount(1)
                        .WithBecause("INC-1234")
                ])));
        LineSet(File.ReadAllText(captivePath))
            .ShouldContain(line => line.Contains("\"source\":"), expectedCount: 1);

        // The bystander pin: the OTHER captive edge (IOrderFormatter) is still red; only IOrderFeed is grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.ShouldReportViolations();
        check.Out
            .ShouldHaveGrandfathered(CaptiveRule, 1)
            .ShouldHaveSingleViolationOnEdge(
                CaptiveRule, kind: "injection",
                source: "MyApp.Web.ReportScheduler", target: "MyApp.Web.IOrderFormatter");

        // The attribution round-trips: --accept-reductions keeps the still-observed IOrderFeed entry (refusing
        // the IOrderFormatter growth) and a second --init leaves the captured section be — byte-identical both ways.
        await ShouldRoundTripByteIdenticalAsync(workspace, captivePath);
    }

    [Fact]
    public async Task BaselineAdd_CatchRule_GrandfathersOneSwallowAndBothBystandersStayRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the catch Migrate rule exceptions/no-general-catch is uncaptured, so both of the Web layer's
        // blanket catches (GRAMMAR §4.8) are current catch violations, each keyed by its own (source, caught)
        // type pair — ReportEndpoint's swallow and ReportPublisher's rethrow, which the plain catch ban does not
        // distinguish between.
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        red.Out.ShouldHaveFailedWith(CatchRule, "source", ["MyApp.Web.ReportEndpoint", "MyApp.Web.ReportPublisher"]);

        // Capture: --init grandfathers both catch identities (T: source × T: caught ForEdge entries, minting the
        // arch/baselines/exceptions/ directory), and the re-check sees the rule green with both baselined.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("exceptions/no-general-catch: captured 2 grandfathered");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        captured.Out.ShouldHaveCaptured(CatchRule, 2);

        // Un-capture (composer as arrangement, the member/construction/injection facts' idiom): a sealed, valid
        // EMPTY section turns both catches red again on a captured rule — the state the valve exists for.
        string catchPath = workspace.PathOf(CatchBaselineFile);
        File.WriteAllText(catchPath, BaselineComposer.Compose((CatchRule, [])));

        // The valve, catch flavor: full-name --source/--target (no T: prefix) resolve the (source, caught)
        // type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", CatchRule,
            "--source", "MyApp.Web.ReportEndpoint", "--target", "System.Exception", "--because", "INC-1234");

        add.ShouldSucceed(
            "exceptions/no-general-catch: added 1 grandfathered entry — MyApp.Web.ReportEndpoint -> System.Exception (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, caught) type pair via ForEdge —
        // ReportPublisher's identical caught type is a distinct source, so it is not swept in.
        File.ReadAllText(catchPath)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (CatchRule, [
                    BaselineEntry.ForEdge(ReportEndpointId, SystemExceptionId)
                        .WithSiteCount(1)
                        .WithBecause("INC-1234")
                ])));
        LineSet(File.ReadAllText(catchPath))
            .ShouldContain(line => line.Contains("\"source\":"), expectedCount: 1);

        // The added swallow now passes, and two bystanders stay red: the in-rule one — ReportPublisher's catch of
        // the very same System.Exception, a distinct (source, caught) identity — and the cross-rule one, the
        // strict Enforce throw rule's BCL throw, which is never ratcheted and so can never be grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.ShouldReportViolations();
        check.Out
            .ShouldHaveGrandfathered(CatchRule, 1)
            .ShouldHaveSingleViolationOnEdge(
                CatchRule, kind: "catch",
                source: "MyApp.Web.ReportPublisher", target: "System.Exception")
            // The Enforce rule carries no baseline, so there is no grandfathered count to read beside it.
            .ShouldHaveSingleViolationOnEdge(
                ThrowRule, kind: "throw",
                source: "MyApp.Domain.OrderApproval", target: "System.InvalidOperationException");

        // The attribution round-trips: --accept-reductions keeps the still-observed swallow entry (refusing the
        // ReportPublisher growth) and a second --init leaves the captured section be — byte-identical both ways.
        await ShouldRoundTripByteIdenticalAsync(workspace, catchPath);
    }

    [Fact]
    public async Task BaselineAdd_ExposeRule_GrandfathersOneSurfacedDataTableAndBystanderStaysRed()
    {
        using var workspace = new TempFixtureWorkspace();

        // Red first: the exposure Migrate rule api/return-dtos is uncaptured, so both Web controllers that return
        // a System.Data.DataTable from a public method (GRAMMAR §4.9) are current expose violations —
        // HomeController.ExportOrders and InvoiceController.ExportInvoices — each keyed by the (source, exposed)
        // type pair. This is the expose analog of the catch-kind E2E above: expose rides the SAME ForEdge valve
        // path (BaselineAddMatcher matches ViolationKind.Expose on both type endpoints, exactly as it does Catch).
        CliResult red = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        red.Out.ShouldHaveFailedWith(ExposeRule, "source", ["MyApp.Web.HomeController", "MyApp.Web.InvoiceController"]);

        // Capture: --init grandfathers both exposure identities (T: source × T: exposed ForEdge entries, minting
        // the arch/baselines/api/ directory), and the re-check sees the rule green with both surfaces baselined.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed("api/return-dtos: captured 2 grandfathered violations.");

        CliResult captured = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        captured.Out.ShouldHaveCaptured(ExposeRule, 2);

        // Un-capture the pair (composer as arrangement, the catch/injection facts' idiom): a sealed, valid EMPTY
        // section turns both surfaces red again on a captured rule — the state the valve exists for.
        string exposePath = workspace.PathOf(ExposeBaselineFile);
        File.WriteAllText(exposePath, BaselineComposer.Compose((ExposeRule, [])));

        // The valve, exposure flavor: full-name --source/--target (no T: prefix) resolve the (source, exposed)
        // type-pair violation, echoed exactly as 'loadbearing check' renders it.
        CliResult add = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll,
            "--add", "--rule", ExposeRule,
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable", "--because", "INC-1234");

        add.ShouldSucceed(
            "api/return-dtos: added 1 grandfathered entry — MyApp.Web.HomeController -> System.Data.DataTable (because: INC-1234).");
        add.Out.ShouldContain("wrote");

        // Composer as oracle: exactly one appended entry keying the (source, exposed) type pair via ForEdge.
        File.ReadAllText(exposePath)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (ExposeRule, [
                    BaselineEntry.ForEdge(HomeId, DataTableId)
                        .WithSiteCount(1)
                        .WithBecause("INC-1234")
                ])));
        LineSet(File.ReadAllText(exposePath))
            .ShouldContain(line => line.Contains("\"source\":"), expectedCount: 1);

        // The bystander pin: the OTHER surfaced DataTable (InvoiceController) is still red; only HomeController is
        // grandfathered.
        CliResult check = await CliRunner.InvokeAsync(
            "check", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--json");
        check.ShouldReportViolations();
        check.Out
            .ShouldHaveGrandfathered(ExposeRule, 1)
            .ShouldHaveSingleViolationOnEdge(
                ExposeRule, kind: "expose",
                source: "MyApp.Web.InvoiceController", target: "System.Data.DataTable");

        // The attribution round-trips: --accept-reductions keeps the still-observed HomeController entry (refusing
        // the InvoiceController growth) and a second --init leaves the captured section be — byte-identical both ways.
        await ShouldRoundTripByteIdenticalAsync(workspace, exposePath);
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
        // The echo names the measure as well as the attribution, and names both ends of it even where the two
        // agree: --add is the only verb that may raise a site count, so what it recorded is never left implied.
        second.ShouldSucceed(
            "data-access/no-inline-sql: entry already baselined — attribution updated and site count re-recorded (2 → 2).");
        second.Out.ShouldContain("wrote");
        // No second entry — the count is unchanged and only the attribution (and the entry's own seal) moved.
        afterSecond.NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                (MigrateRule,
                [
                    BaselineEntry.ForEdge(HomeId, DataTableId)
                        .WithSiteCount(2)
                        .WithBecause("second"),
                    BaselineEntry.ForEdge(InvoiceId, DataTableId)
                        .WithSiteCount(2)
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
        add.ShouldSucceed();
        byte[] snapshot = File.ReadAllBytes(migratePath);

        // Both entries are still observed, so accept-reductions removes nothing and preserves the attribution.
        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.ShouldSucceed();
        File.ReadAllBytes(migratePath)
            .ShouldBe(snapshot);

        // The Migrate rule is already captured, so --init leaves the attributed entry byte-identical, seal and all.
        CliResult init = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        init.ShouldSucceed();
        File.ReadAllBytes(migratePath)
            .ShouldBe(snapshot);
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

        unknown.ShouldRefuseWith("rule 'nope/nothing' is not in the spec.");

        nonRatcheted.ShouldRefuseWith(
            "rule 'layering/domain-independent' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");

        uncaptured.ShouldRefuseWith(
            "no baseline section for 'legacy/billing/containment' — run 'loadbearing baseline --init' first.");

        noMatch.ShouldRefuseWith(
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

    /// <summary>
    ///     Asserts the baseline file at <paramref name="path" /> survives both writing verbs byte for byte:
    ///     <c>--accept-reductions</c> keeps the still-observed entry (refusing the bystander's growth), and a
    ///     second <c>--init</c> leaves the captured section be. Attribution is the fragile half — it is the
    ///     one field neither verb composes for itself.
    /// </summary>
    private static async Task ShouldRoundTripByteIdenticalAsync(TempFixtureWorkspace workspace, string path)
    {
        byte[] snapshot = File.ReadAllBytes(path);

        CliResult accept = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--accept-reductions");
        accept.ShouldSucceed();
        File.ReadAllBytes(path)
            .ShouldBe(snapshot);

        CliResult reinit = await CliRunner.InvokeAsync(
            "baseline", workspace.SolutionPath, "--spec", CliRunner.ViolatedSpecDll, "--init");
        reinit.ShouldSucceed();
        File.ReadAllBytes(path)
            .ShouldBe(snapshot);
    }

    private static HashSet<string> LineSet(string text)
    {
        return text.NormalizedLines()
            .Split('\n')
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }
}

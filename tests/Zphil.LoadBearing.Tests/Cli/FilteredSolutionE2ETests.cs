using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     A real <c>.slnf</c> driven through MSBuild, end to end — the load the rest of the filter tests could
///     only approximate, since every one of them is an <c>AdhocWorkspace</c> or a pure string.
/// </summary>
/// <remarks>
///     <para>
///         <b>The bed is MyApp, reached by relative path.</b> The filters live in their own directory
///         because discovery prefers a solution over a filter standing beside it, so a <c>.slnf</c> next to
///         <c>MyApp.sln</c> is one no walk-up could resolve to; and pointing back at
///         <c>../TestSolutions/MyApp/MyApp.sln</c> is also what proves the parser's two resolution bases
///         are real: the solution resolves against the filter's directory and every project
///         entry against the solution's. They therefore run <em>in place</em> from the build output rather
///         than through a leased copy, which would strand the relative path. The one exception is the
///         write-anchor row, whose filter is written per-run and names its solution absolutely, because
///         showing where a rendered file lands means letting one be written.
///     </para>
///     <para>
///         <b>Billing is the leaf of the reference chain</b> (<c>Domain → Web → Billing</c>), which is the
///         only reason a narrowing fixture is possible at all: a filter is a seed set, so Roslyn loads what
///         it selects plus the transitive closure, and only a selection at the bottom of the chain leaves
///         anything out. <c>DomainAndWeb.slnf</c> selects two of three and narrows nothing — Billing arrives
///         transitively — and is the negative fixture that keeps the stamps from being unconditional.
///     </para>
///     <para>
///         <b>Cold invocations throughout</b> (<see cref="CliRunner.InvokeColdAsync(string[])" />, or the
///         runner directly for the verbs the CLI does not expose): the subject here is the load boundary,
///         where the narrowing is measured as declared minus loaded, so every row loads a filter of its own
///         rather than reading one some earlier row left warm. The pool no longer drops the load report —
///         it composed its handle without <c>FailedProjects</c> and <c>UncheckedProjects</c> until this
///         arc — but a run that shares a load still proves nothing about one.
///     </para>
///     <para>
///         <b>Both answers a filter can get.</b> <c>check</c>, <c>status</c>, <c>graph</c> and
///         <c>context</c> report over the smaller universe and stamp what was left out;
///         <c>
///             baseline
///             --init
///         </c>
///         , <c>baseline --accept-reductions</c> and <c>render</c> read absence as evidence and
///         write files that outlive the run, so they refuse — and every one of those rows asserts that both
///         the filter's directory and the referenced solution's bed are as they were, the bed because that is
///         where the output would land and where <c>render</c>'s targets already exist to be overwritten.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class FilteredSolutionE2ETests
{
    // The stamp's first line, and then its evidence: the two declared projects a Billing-only filter never
    // checks, shown from the referenced solution's directory the way every path in this output is — the
    // filter's own name still leads, because that is the file the operator passed.
    private const string NarrowingLede =
        "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.";

    private const string UncheckedEvidence =
        "  MyApp.Domain/MyApp.Domain.csproj\n"
        + "  MyApp.Web/MyApp.Web.csproj";

    private const string CheckStamp =
        NarrowingLede + "\n" + UncheckedEvidence + "\n"
        + "The verdict below covers only the projects that loaded, so a clean result here is not a clean "
        + "solution.";

    private const string StatusStamp =
        NarrowingLede + "\n" + UncheckedEvidence + "\n"
        + "The burndown below counts only the projects that loaded: an unchecked project contributes no "
        + "violations, so every count reads low.";

    private const string GraphStamp =
        NarrowingLede + "\n" + UncheckedEvidence + "\n"
        + "The survey below describes only the projects that loaded, so a project or reference edge absent "
        + "from it may simply be out of view.";

    private const string ContextStamp =
        NarrowingLede + "\n" + UncheckedEvidence + "\n"
        + "The answer below covers only the projects that loaded: a scope card from an unchecked project "
        + "places nowhere, so \"no architecture scope covers\" cannot be trusted for paths under them.";

    // The two refusals write their own lede — what they refuse is the point of the sentence — so neither
    // shares NarrowingLede, and both end in the fix that stands in for the opt-out flag they do not have.
    private const string BaselineRefusal =
        "error: 'BillingOnly.slnf' narrowed this run — 2 projects the solution declares were not checked, so "
        + "no baseline was written: a baseline captured through a filter signs off debt in projects it never "
        + "measured, and --accept-reductions deletes real entries as \"no longer occurring\" when the only "
        + "thing that changed is that a project stopped being checked:\n"
        + UncheckedEvidence + "\n"
        + "Run baseline against the solution the filter references rather than through the filter.";

    private const string RenderRefusal =
        "error: 'BillingOnly.slnf' narrowed this run — 2 projects the solution declares were not checked, so "
        + "nothing was rendered: rendered files are committed context, a card from an unchecked project "
        + "places nowhere and would be dropped from the committed files, and --diagram would draw a survey "
        + "missing whole projects:\n"
        + UncheckedEvidence + "\n"
        + "Run render against the solution the filter references rather than through the filter.";

    // The per-rule twin of the stamp: one line, on every surface a report reaches, for a rule whose subject
    // the filter left out. It counts the unchecked projects and never lists them — the stamp above the
    // report already does, one place to read them rather than one copy per rule.
    private const string RuleSkipReason =
        "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked, and this "
        + "rule's subject matched no type in the projects that loaded — so it was not measured, and this is "
        + "not a clean result for it.";

    // The one fragment every "this run narrowed nothing" row asserts absent. Asserting the whole stamp
    // absent would pass for a stamp whose wording had merely drifted.
    private const string AnyNarrowing = "narrowed this run";

    // The same two projects the stamps name, as the documents carry them.
    private static readonly string[] TheTwoUncheckedProjects =
    [
        "MyApp.Domain/MyApp.Domain.csproj",
        "MyApp.Web/MyApp.Web.csproj"
    ];

    private static string FilterDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FilteredSolutions");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Check_NarrowingFilter_StampsTheNarrowingAboveTheVerdict()
    {
        // The field failure this whole arc exists to remove: check over a filter said nothing about the two
        // projects it never looked at, so a verdict over a subset read as a verdict over the solution.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache");

        // Exit 0: the clean spec is clean over this solution, and it stays clean read through a filter. The
        // rule whose subject the filter dropped is skipped rather than red — see the row below — so the only
        // thing narrowing changes about the verdict is what the stamp above it says the verdict covers.
        result.ShouldSucceed();
        result.Out.NormalizedLines()
            .ShouldStartWith(CheckStamp + "\n\n"); // a blank line separates the stamp from the verdict it scopes
    }

    [Fact]
    public async Task Check_NarrowingFilter_SkipsTheRuleWhoseSubjectTheFilterDropped()
    {
        // The second half of the field failure, and the sharper one: the clean spec's ratcheted rule is
        // scoped to MyApp.Web, which this filter leaves out, so it selected nothing and red with "the
        // subject selection matched no solution-declared types" — a spec defect for a spec that has none,
        // and exit 1 for a solution that is clean. It skips now, and the reason names the filter that did it.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache");

        result.ShouldSucceed();
        string report = result.Out.NormalizedLines();
        report.ShouldContain("\nskip data-access/no-inline-sql — ");
        report.ShouldContain("\n  skipped: " + RuleSkipReason);
        // The whole verdict, so a skip that quietly became a pass — or a second rule that started skipping —
        // cannot hide behind the two fragments above.
        report.ShouldContain("Checked 3 rules: 2 passed, 0 failed, 1 skipped (0 violations, 1 warnings).");
    }

    [Fact]
    public async Task CheckJson_NarrowingFilter_CarriesTheSkipAndItsReasonPerRule()
    {
        // --json is the only channel arch_check has, and a rule that is neither passed nor failed has to be
        // legible there too: an agent reading "status": "passed" for this rule would be told the run checked
        // something it never looked at.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement rule = CheckJson.Rule(document, "data-access/no-inline-sql");
        rule.GetProperty("status")
            .GetString()
            .ShouldBe("skipped");
        rule.GetProperty("skipReason")
            .GetString()
            .ShouldBe(RuleSkipReason);
    }

    [Fact]
    public async Task Status_NarrowingFilter_StampsTheBurndownWithItsOwnTail()
    {
        // status's counts are the thing a narrowed universe corrupts most quietly: an unchecked project
        // declares no types and so contributes no violations, which reads as progress.
        CliResult result = await CliRunner.InvokeColdAsync(
            "status", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache");

        result.ShouldSucceed();
        result.Out.NormalizedLines()
            .ShouldStartWith(StatusStamp + "\n\n");
    }

    [Fact]
    public async Task Graph_NarrowingFilter_StampsTheSurveyWithItsOwnTail()
    {
        // graph needs no spec and is the first verb a stranger runs, where a project out of view reads as a
        // codebase that simply does not have one.
        CliResult result = await CliRunner.InvokeColdAsync("graph", BillingOnlyFilter(), "--no-cache");

        result.ShouldSucceed();
        result.Out.NormalizedLines()
            .ShouldStartWith(GraphStamp + "\n\n");
        result.Out.ShouldContain("MyApp.Legacy.Billing"); // the survey it does have still follows
    }

    [Fact]
    public async Task CheckJson_NarrowingFilter_CarriesTheUncheckedProjectsAndNoStamp()
    {
        // --json owns stdout, so the stamp is suppressed and the document carries the same fact instead —
        // the only channel arch_check has, since the MCP surface runs this verb with --json forced on.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        using JsonDocument document = result.ShouldHaveJsonStdout();
        // The stamp's own lede rather than the bare phrase: this document quotes the narrowing legitimately,
        // once per rule the filter emptied, so "narrowed this run" no longer tells a stamp from a skip
        // reason. The lede's full stop does — a reason continues past it with a comma.
        result.Out.ShouldNotContain(NarrowingLede);
        CheckJson.Strings(document, "uncheckedProjects")
            .ShouldBe(TheTwoUncheckedProjects);
        // It scopes the verdict rather than overturning it: nothing failed to load.
        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task StatusJson_NarrowingFilter_CarriesTheUncheckedProjectsAndNoStamp()
    {
        CliResult result = await CliRunner.InvokeColdAsync(
            "status", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache", "--json");

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        result.Out.ShouldNotContain(AnyNarrowing);
        CheckJson.Strings(document, "uncheckedProjects")
            .ShouldBe(TheTwoUncheckedProjects);
    }

    [Fact]
    public async Task GraphJson_NarrowingFilter_CarriesTheUncheckedProjectsAndNoStamp()
    {
        CliResult result = await CliRunner.InvokeColdAsync(
            "graph", BillingOnlyFilter(), "--no-cache", "--json");

        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        result.Out.ShouldNotContain(AnyNarrowing);
        CheckJson.Strings(document, "uncheckedProjects")
            .ShouldBe(TheTwoUncheckedProjects);
    }

    [Fact]
    public async Task CheckSarif_NarrowingFilter_CarriesOneWarningNotification()
    {
        // The third render target. Code scanning reads neither stdout nor an exit code, so a clean SARIF
        // over a filter would close every alert the unchecked projects would have raised.
        using TempDirectory temp = TestTempRoot.Fresh("filtered-sarif");
        string sarifPath = temp.PathOf("filtered.sarif");

        CliResult result = await CliRunner.InvokeColdAsync(
            "check", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--no-cache", "--sarif", sarifPath);

        result.ShouldSucceed(); // clean over the subset, which is exactly why the notification has to be there
        string sarif = File.ReadAllText(sarifPath);
        sarif.ShouldContain("\"level\": \"warning\"");
        sarif.ShouldContain(
            "A solution filter narrowed this run: 2 projects the solution declares were not checked, so "
            + "these results cover part of the solution: "
            + "MyApp.Domain/MyApp.Domain.csproj, "
            + "MyApp.Web/MyApp.Web.csproj");
    }

    [Fact]
    public async Task StatusAndGraph_FilterThatNarrowsNothing_SayNothingAboutNarrowing()
    {
        // The negative fixture, and the reason the stamp is measured from the load rather than read from the
        // filter text: this one selects two projects of three and checks all three, because the third is
        // pulled in transitively. A stamp derived from the selection would name a project the run checked.
        // A spec-ful verb and the spec-less one, so the suppression is proven on both sides of the seam.
        string filter = Path.Combine(FilterDirectory, "DomainAndWeb.slnf");

        CliResult status = await CliRunner.InvokeColdAsync(
            "status", filter, "--spec", CliRunner.CleanSpecDll, "--no-cache");
        CliResult graph = await CliRunner.InvokeColdAsync("graph", filter, "--no-cache", "--json");

        status.ShouldSucceed();
        status.Out.ShouldNotContain(AnyNarrowing);
        graph.ShouldSucceed("MyApp.Legacy.Billing"); // the transitively-loaded third project
        using JsonDocument document = graph.ShouldHaveJsonStdout();
        // Absent, not empty: a slot rendered as [] on every unfiltered run would move all seven goldens.
        document.RootElement.TryGetProperty("uncheckedProjects", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_FilterThatNarrowsNothing_ResolvesBaselinesAgainstTheReferencedSolution()
    {
        // The false red an anchor at the filter's own directory produced, and the reason the anchor moved.
        // The clean spec grandfathers its two DataTable sites through arch/clean-baseline.json, a
        // solution-relative convention path; resolved beside the filter that file is simply absent, so both
        // sites went red and check exited 1. This filter narrows nothing, so no stamp fired to explain it —
        // a run that changed nothing about the universe must reach the solution's own verdict.
        string filter = Path.Combine(FilterDirectory, "DomainAndWeb.slnf");

        CliResult result = await CliRunner.InvokeColdAsync(
            "check", filter, "--spec", CliRunner.CleanSpecDll, "--no-cache");

        result.ShouldSucceed();
        result.Out.ShouldNotContain(AnyNarrowing);
    }

    [Fact]
    public async Task GraphJson_EmptyProjectsFilter_SurveysTheWholeSolutionAndSaysNothing()
    {
        // An empty projects array is Roslyn's own spelling of "no filtering at all", not of "select
        // nothing". Read the other way it would load nothing and report the whole solution unchecked, which
        // is why the survey has to show all three projects rather than merely omit the slot.
        CliResult result = await CliRunner.InvokeColdAsync(
            "graph", Path.Combine(FilterDirectory, "Empty.slnf"), "--no-cache", "--json");

        result.ShouldSucceed("MyApp.Domain", "MyApp.Web", "MyApp.Legacy.Billing");
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("uncheckedProjects", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_MalformedFilter_RefusesNamingTheFile()
    {
        // Roslyn throws a bare exception for every filter fault alike, so without the mapping this reached
        // the operator as a stack trace over a file it never named.
        CliResult result = await CliRunner.InvokeColdAsync(
            "check", Path.Combine(FilterDirectory, "Malformed.slnf"), "--spec", CliRunner.CleanSpecDll,
            "--no-cache");

        result.ShouldRefuseWith("Could not read the solution filter", "Malformed.slnf");
        result.Err.ShouldContain(
            "A filter must be well-formed JSON whose 'solution' path resolves to a .sln or .slnx, and every "
            + "project it lists must be a member of that solution.");
    }

    [Fact]
    public async Task Context_NarrowingFilter_StampsTheAnswerAboveThePointerLine()
    {
        // Context never gates — it is a lookup an agent runs mid-edit — but its pointer line is exactly the
        // answer a narrowed universe turns into a false all-clear, and stdout is its only channel. Driven
        // through the runner directly, because there is no CLI verb to invoke.
        var output = new StringWriter();

        int exit = await new ContextRunner(output).RunAsync(
            new ContextRequest("MyApp.Legacy.Billing", BillingOnlyFilter(), CliRunner.CleanSpecDll, FilterDirectory),
            Ct);

        exit.ShouldBe(0); // a smaller true answer, so it answers
        string answer = output.ToString()
            .NormalizedLines();
        answer.ShouldStartWith(ContextStamp + "\n\n");
        answer.ShouldContain("No architecture scope covers"); // the answer it does have still follows
    }

    [Fact]
    public async Task Context_FilterThatNarrowsNothing_SaysNothingAboutNarrowing()
    {
        var output = new StringWriter();

        int exit = await new ContextRunner(output).RunAsync(
            new ContextRequest(
                "MyApp.Legacy.Billing", Path.Combine(FilterDirectory, "DomainAndWeb.slnf"),
                CliRunner.CleanSpecDll, FilterDirectory),
            Ct);

        exit.ShouldBe(0);
        var answer = output.ToString();
        answer.ShouldNotContain(AnyNarrowing);
        answer.ShouldStartWith("No architecture scope covers");
    }

    [Fact]
    public async Task BaselineInit_NarrowingFilter_RefusesWithoutWritingAnything()
    {
        // The line the arc draws: a baseline is the team's signature on its debt, and --init through a filter
        // signs "zero debt" for every rule whose subject the filter left out. A filtered run anchors at the
        // solution it references, so the baseline this would write lands in MyApp's own arch/ — beside the
        // baseline a real team committed, which is exactly what makes writing it unrecoverable.
        string[] before = FilesWhereAWriteWouldLand();

        CliResult result = await CliRunner.InvokeColdAsync(
            "baseline", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--init");

        result.ShouldRefuseWith(BaselineRefusal.Split('\n'));
        result.Out.ShouldBeEmpty(); // it refused before the first captured/unchanged line
        FilesWhereAWriteWouldLand()
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineAcceptReductions_NarrowingFilter_RefusesWithTheSameWordsAndWritesNothing()
    {
        // The mode the refusal's own lede names, and it needs no captured baseline to arrange: the refusal
        // sits before extraction, so the mode never reaches the file it would have pruned. That is the point —
        // a project that stopped being checked looks exactly like a violation that stopped occurring.
        string[] before = FilesWhereAWriteWouldLand();

        CliResult result = await CliRunner.InvokeColdAsync(
            "baseline", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--accept-reductions");

        result.ShouldRefuseWith(BaselineRefusal.Split('\n'));
        result.Out.ShouldBeEmpty();
        FilesWhereAWriteWouldLand()
            .ShouldBe(before);
    }

    [Fact]
    public async Task BaselineAdd_NarrowingFilter_RidesThroughToItsOwnValidation()
    {
        // --add is deliberately outside the refusal: it records one violation the run did see and claims
        // nothing about what it did not. On this bed it cannot go on to write, because the clean spec's only
        // ratcheted rule is scoped to MyApp.Web and loses its subject with it — so what proves the branch is
        // that the message is --add's own, reached after the load, the gate and the whole check.
        CliResult result = await CliRunner.InvokeColdAsync(
            "baseline", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll, "--add",
            "--rule", "data-access/no-inline-sql", "--because", "Legacy import path.",
            "--source", "MyApp.Web.HomeController", "--target", "System.Data.DataTable");

        result.ShouldRefuseWith("cannot add to rule 'data-access/no-inline-sql'");
        result.Err.ShouldNotContain(AnyNarrowing);
    }

    [Fact]
    public async Task Render_NarrowingFilter_RefusesWithoutWritingAnything()
    {
        // The other stakes verb. What render writes is committed context that outlives the run: every card
        // from an unchecked project would be silently absent from a file a reader then trusts — and under the
        // referenced solution's anchor the file it would overwrite is MyApp's own committed AGENTS.md.
        string[] before = FilesWhereAWriteWouldLand();

        CliResult result = await CliRunner.InvokeColdAsync(
            "render", BillingOnlyFilter(), "--spec", CliRunner.CleanSpecDll);

        result.ShouldRefuseWith(RenderRefusal.Split('\n'));
        result.Out.ShouldBeEmpty(); // it refused before the first wrote/unchanged line
        FilesWhereAWriteWouldLand()
            .ShouldBe(before);
    }

    [Fact]
    public async Task Render_FilterThatNarrowsNothing_WritesBesideTheReferencedSolution()
    {
        // The write side of the anchor, and the one row here that cannot run in place: showing where a
        // rendered file lands means letting one be written, which the shared bed must never see. Deleting the
        // copy's AGENTS.md first is what makes the answer unambiguous — the fixture ships one, so a file that
        // is merely present proves nothing about who put it there.
        using var workspace = new TempFixtureWorkspace();
        using TempDirectory filterDirectory = TestTempRoot.Fresh("filter-write-anchor");
        string filter = WriteWholeSolutionFilter(filterDirectory, workspace.SolutionPath);
        string renderedTarget = workspace.PathOf("AGENTS.md");
        File.Delete(renderedTarget);

        CliResult result = await CliRunner.InvokeColdAsync("render", filter, "--spec", CliRunner.RenderSpecDll);

        result.ShouldSucceed();
        File.Exists(renderedTarget)
            .ShouldBeTrue("render wrote nothing beside the solution the filter references.");
        Directory.EnumerateFiles(filterDirectory.Path, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ShouldBe(["Whole.slnf"]);
    }

    /// <summary>
    ///     The narrowing filter's path, with the arrange-time guard that keeps this class honest: MyApp must
    ///     still declare exactly three projects, and the filter must still select nothing but the leaf of
    ///     their reference chain. Adding a fourth member to the solution, or a second project to the filter,
    ///     is the single edit that would leave every fact here passing while exercising no narrowing at all,
    ///     so it fails loudly here instead.
    /// </summary>
    private static string BillingOnlyFilter()
    {
        string solution = File.ReadAllText(CliRunner.MyAppSolution);
        int declared = solution.Split(".csproj").Length - 1;
        declared.ShouldBe(
            3,
            $"MyApp.sln now declares {declared} projects rather than three, so the paths "
            + "FilteredSolutionE2ETests pins as unchecked are no longer the solution's whole remainder.");

        string filterPath = Path.Combine(FilterDirectory, "BillingOnly.slnf");
        string filter = File.ReadAllText(filterPath);
        filter.ShouldContain(
            "MyApp.Legacy.Billing/MyApp.Legacy.Billing.csproj",
            "BillingOnly.slnf no longer selects the leaf of the reference chain, so it narrows nothing and "
            + "every stamp this class pins would be absent.");
        filter.ShouldNotContain("MyApp.Domain");
        filter.ShouldNotContain("MyApp.Web");

        return filterPath;
    }

    // The whole write-nothing assertion, over both directories a leaked write could reach: the filter's own,
    // and — since a filtered run anchors at the solution it references — MyApp's bed, where a baseline would
    // be created and where render's targets already exist. Size and mtime ride along because AGENTS.md and
    // ARCHITECTURE.md are already there, so a listing of names alone could not tell an overwrite from a
    // refusal. bin/obj are left out: a design-time build touches them legitimately mid-row, and no verb here
    // writes to them.
    private static string[] FilesWhereAWriteWouldLand()
    {
        string[] roots = [FilterDirectory, Path.GetDirectoryName(CliRunner.MyAppSolution)!];
        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => !TempFixtureWorkspace.IsBuildArtifact(path))
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Describe(string path)
    {
        var file = new FileInfo(path);
        return $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>
    ///     A filter naming its solution by absolute path, in a directory of its own outside the copy — the
    ///     arrangement that lets a write-anchor row use a leased copy at all, since every other filter here is
    ///     pinned in place by a relative path. Its <c>projects</c> array is empty, which is Roslyn's spelling
    ///     of "no filtering at all", so the run gets past the refusal the rows above pin.
    /// </summary>
    private static string WriteWholeSolutionFilter(TempDirectory directory, string solutionPath)
    {
        string filter = directory.PathOf("Whole.slnf");
        string declaredPath = solutionPath.Replace('\\', '/');
        File.WriteAllText(filter, $$"""{ "solution": { "path": "{{declaredPath}}", "projects": [] } }""");
        return filter;
    }
}

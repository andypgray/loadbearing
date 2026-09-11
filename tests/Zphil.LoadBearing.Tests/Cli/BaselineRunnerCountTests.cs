using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The site measure through all three <c>baseline</c> writing modes, over an in-memory fast-tier check —
///     no workspace, like <see cref="BaselineRunnerAddTests" /> beside it. What each mode may do to a count
///     is the whole subject: every write records what it observed, <c>--accept-reductions</c> may only
///     tighten (record where there was nothing, lower where the observation came in under), and
///     <c>--add</c> is the one route by which a count goes up — attributed, one entry at a time.
/// </summary>
/// <remarks>
///     <para>
///         Three arms here are not about counting at all but sit beside it, and each is pinned because
///         nothing else would notice it moving. Duplicate identities fold: a symbol ID names a name rather
///         than a node (GRAMMAR §4.3), so two violations can share one identity, and a write now emits one
///         entry line covering the larger of them where it used to emit the line twice. <c>--init</c>
///         declines to write a file it captured nothing into — without which a write, always composing the
///         current schema version, would upgrade a legacy file in place while echoing it unchanged. And
///         neither mode creates a file it put nothing into: <c>--accept-reductions</c> over an uncaptured
///         rule, and <c>--init</c> over a rule it cannot capture, each say so and write nothing.
///     </para>
///     <para>
///         The runner is driven through <see cref="BaselineRunner.AddEntry" /> and
///         <see cref="BaselineRunner.ApplyFiles" />, the two seams that take a report rather than load a
///         workspace, so a row costs a compilation instead of an MSBuild solution load.
///     </para>
/// </remarks>
public sealed class BaselineRunnerCountTests : IDisposable
{
    private const string RuleId = "data-access/ledger-behind-repository";
    private const string BaselineFile = "ledger.json";
    private const string ControllerId = "T:App.Web.ReportController";
    private const string ExportControllerId = "T:App.Web.ExportController";
    private const string LedgerId = "T:App.Data.Ledger";

    // Two sites under the one identity: the ledger is named on the signature line and again on the line
    // that constructs it.
    private const string TwoSiteSource = """
                                         namespace App.Data { public class Ledger {} }
                                         namespace App.Web
                                         {
                                             public class ReportController
                                             {
                                                 public App.Data.Ledger Load()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }
                                         }
                                         """;

    // The same controller after a third mention lands in it — one more line naming the same pair, which is
    // growth inside an identity the baseline already blesses.
    private const string ThreeSiteSource = """
                                           namespace App.Data { public class Ledger {} }
                                           namespace App.Web
                                           {
                                               public class ReportController
                                               {
                                                   public App.Data.Ledger Load()
                                                   {
                                                       return new App.Data.Ledger();
                                                   }

                                                   public App.Data.Ledger Reload()
                                                   {
                                                       return Load();
                                                   }
                                               }
                                           }
                                           """;

    // Two controllers reaching the same ledger over two lines each — two identities under one rule, for the
    // row whose subject is how many entries a run moved rather than how many sites sit under one of them.
    private const string TwoPairSource = """
                                         namespace App.Data { public class Ledger {} }
                                         namespace App.Web
                                         {
                                             public class ReportController
                                             {
                                                 public App.Data.Ledger Load()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }

                                             public class ExportController
                                             {
                                                 public App.Data.Ledger Fetch()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }
                                         }
                                         """;

    // No web layer at all, so the rule's subject is empty: the one state a write cannot capture.
    private const string NoWebSource = """
                                       namespace App.Data { public class Ledger {} }
                                       """;

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-runner-count");

    private static BaselineEntry Pair => BaselineEntry.ForEdge(ControllerId, LedgerId);

    private static BaselineEntry ExportPair => BaselineEntry.ForEdge(ExportControllerId, LedgerId);

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void AddEntry_NewEntry_RecordsTheSitesObservedUnderItsIdentity()
    {
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId));

        var output = new StringWriter();
        int exit = Runner(output)
            .AddEntry(AddRequest("INC-1"), report, _temp.Path);

        exit.ShouldBe(0);
        // The valve grandfathers a pair AND says how much of it: both of the controller's mentions ride the
        // one entry, and the entry records that it is two rather than any.
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-1")));
    }

    [Fact]
    public void AddEntry_PresentUncountedEntry_RecordsTheCountAndNamesTheMoveFromUncounted()
    {
        // The entry an older write left behind: attributed, but with no measure on it at all.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithBecause("INC-1")));

        var output = new StringWriter();
        Runner(output)
            .AddEntry(AddRequest("INC-2"), report, _temp.Path);

        output.ToString()
            .ShouldContain(
                "data-access/ledger-behind-repository: entry already baselined — attribution updated and site count re-recorded (uncounted → 2).");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-2")));
    }

    [Fact]
    public void AddEntry_PresentEntryThatGrew_ReRecordsTheCountAndNamesBothEndsOfTheMove()
    {
        // The valve's whole reason for touching the measure: the pair has gained a site, which the ratchet
        // reds, and --add is the only verb that may raise the allowance to cover it.
        CheckReport report = Check(ThreeSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(
            RuleId,
            Pair.WithSiteCount(2)
                .WithBecause("INC-1")));

        var output = new StringWriter();
        Runner(output)
            .AddEntry(AddRequest("INC-2"), report, _temp.Path);

        output.ToString()
            .ShouldContain(
                "data-access/ledger-behind-repository: entry already baselined — attribution updated and site count re-recorded (2 → 3).");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(3)
                    .WithBecause("INC-2")));
    }

    [Fact]
    public void AcceptReductions_UncountedEntry_RecordsTheCountAndUpgradesTheFile()
    {
        // The designed upgrade path off the legacy format: the entry is unchanged in identity and
        // attribution, gains the measure it never carried, and the envelope comes forward with it.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, Pair.WithBecause("INC-1")));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: recorded the site count on 1 entry.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-1")));
    }

    [Fact]
    public void AcceptReductions_TwoUncountedEntries_InflectTheEntryNounForTheCount()
    {
        // The sentence counts entries, so the count has to reach the noun — one entry, two entries. Pinned
        // through the verb that renders it, because that is where a regression would be read.
        CheckReport report = Check(TwoPairSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, ExportPair, Pair));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: recorded the site count on 2 entries.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                ExportPair.WithSiteCount(2),
                Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_EntryObservedUnderItsCount_LowersItToWhatTheRunSaw()
    {
        // A real reduction inside a pair that still exists: the identity is not stale, so only the measure
        // can carry the news that a site was fixed.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(3)));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: lowered the site count on 1 entry.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_EntryObservedOverItsCount_RefusesTheGrowthAndLeavesTheEntryAlone()
    {
        // The mode only ever tightens, so growth is refused here exactly as a new identity is: it accepted
        // nothing, and it says so beside the refusal rather than quietly widening the allowance.
        CheckReport report = Check(ThreeSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: nothing to accept.");
        echo.ShouldContain(
            "data-access/ledger-behind-repository: refused site growth on 1 entry — a grandfathered pair grows only via 'loadbearing baseline --add', one attributed entry at a time.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_NothingMoved_KeepsTheNothingToAcceptVerdictAlone()
    {
        // The verdict every kind of acceptance has to clear, on a run where none of them fired: no entry
        // removed, no count lowered, none first recorded.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
        byte[] before = File.ReadAllBytes(path);

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: nothing to accept.");
        echo.ShouldNotContain("site count");
        echo.ShouldNotContain("refused");
        File.ReadAllBytes(path)
            .ShouldBe(before);
    }

    [Fact]
    public void Init_TwoViolationsSharingOneIdentity_WriteOneEntryCoveringTheLargerCount()
    {
        // A symbol ID names a name, not a node (GRAMMAR §4.3), so one pair can arrive as two violations —
        // here the same controller compiled into two projects, reaching a ledger of the same full name in
        // each. Synthetic nodes, because the shape is about identity rather than about any real codebase.
        RuleResult result = SharedIdentityResult(observed: [2, 3]);

        string echo = Init([result]);

        // One line, not two — and the allowance covers the larger of the two, because anything less would
        // red the very state the capture was taken from.
        echo.ShouldContain("data-access/ledger-behind-repository: captured 1 grandfathered violation.");
        Read(_temp.PathOf(BaselineFile))
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(3)));
    }

    [Fact]
    public void Init_FullyCapturedLegacyFile_WritesNothingAndLeavesTheVersionWhereItWas()
    {
        // --init reports "unchanged" here, so it must also BE unchanged: a write would compose the current
        // schema version and silently upgrade a file the operator was told nobody touched.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, Pair));
        byte[] before = File.ReadAllBytes(path);

        string echo = Init(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: already captured (1 entries) — unchanged.");
        echo.ShouldContain($"unchanged {BaselineFile}");
        File.ReadAllBytes(path)
            .ShouldBe(before);
        Read(path)
            .ShouldContain($"\"schemaVersion\": {BaselineFormat.LegacySchemaVersion},");
    }

    [Fact]
    public void AcceptReductions_UncapturedRule_WritesNoFile()
    {
        // The mode tells the operator to run --init first. It must not create, in the same breath, the file
        // that sentence is about: nothing was tightened, so there is nothing to write, and a section-less
        // file would sit in the tree under a "wrote" line contradicting it.
        CheckReport report = Check(TwoSiteSource);

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: no baseline section — run 'loadbearing baseline --init' first.");
        echo.ShouldNotContain("wrote");
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Init_RuleWithNothingCapturable_WritesNoFile()
    {
        // An empty subject is unbaselinable and is echoed as skipped; the file it would have gone into is not
        // created around that absence.
        CheckReport report = Check(NoWebSource);

        string echo = Init(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: cannot capture — the rule has an empty subject or an evaluation error; skipped.");
        echo.ShouldNotContain("wrote");
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    // One ratcheted reference rule over the two synthetic namespaces, checked against an empty baseline so
    // every current violation surfaces as the state a write would capture — the report shape every mode takes.
    private static CheckReport Check(string source)
    {
        return ArchChecker.Check(Model(), CompilationFactory.Extract(source), BaselineIndex.Empty);
    }

    private static ArchitectureModel Model()
    {
        return Checker.Model(arch => arch.Rule(RuleId)
            .Migrate(
                "the web layer reaches the ledger directly",
                arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Baseline(BaselineFile)
            .Because("b"));
    }

    // Two reference violations of the same rule whose endpoints wear the same symbol IDs under different
    // project attributions — distinct nodes, one baseline identity — carrying the site counts named.
    private static RuleResult SharedIdentityResult(int[] observed)
    {
        List<Violation> violations = observed
            .Select((sites, index) => Violation.Reference(
                SyntheticNodes.Type("App.Web.ReportController", ControllerId, $"Web{index}"),
                SyntheticNodes.Type("App.Data.Ledger", LedgerId, $"Data{index}"),
                SyntheticNodes.Sites("ReportController.cs", sites)))
            .ToList();

        return new RuleResult(Model().Rule(RuleId), RuleStatus.Failed, violations);
    }

    private BaselineRunner Runner(TextWriter output)
    {
        return new BaselineRunner(output, TextWriter.Null);
    }

    private BaselineRequest AddRequest(string because)
    {
        return BaselineRequests.Add(RuleId, because, "App.Web.ReportController", "App.Data.Ledger", _temp.Path);
    }

    // --init and --accept-reductions through the one seam they share, handing back what the runner said.
    private string Init(IEnumerable<RuleResult> results)
    {
        return Apply(BaselineRequests.Init(_temp.Path), results);
    }

    private string AcceptReductions(IEnumerable<RuleResult> results)
    {
        return Apply(BaselineRequests.AcceptReductions(_temp.Path), results);
    }

    private string Apply(BaselineRequest request, IEnumerable<RuleResult> results)
    {
        var output = new StringWriter();
        Runner(output)
            .ApplyFiles(request, results, _temp.Path);
        return output.ToString();
    }

    private string WriteBaseline(string content)
    {
        return _temp.WriteFile([BaselineFile], content);
    }

    private static string Read(string path)
    {
        return File.ReadAllText(path)
            .NormalizedLines();
    }
}

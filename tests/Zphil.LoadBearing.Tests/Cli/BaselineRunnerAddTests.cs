using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Roslyn.Baselines;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The <c>baseline --add</c> runner path (<see cref="BaselineRunner.AddEntry" />) over an in-memory
///     fast-tier check — no workspace, unlike the fixture e2e.
/// </summary>
/// <remarks>
///     Pins the member regression: a
///     <see cref="ViolationKind.MemberUse" /> violation resolved by a full-name <c>--target</c> carries a
///     null <c>Target</c> slot, so the added-entry echo must render through the shared full-name form
///     (<c>Source -&gt; member display</c>, GRAMMAR §4.5) and the appended entry must key the member's
///     <c>P:</c> symbol ID. Also the construction reach (GRAMMAR §4.5, §4.3): a
///     <see cref="ViolationKind.Construction" /> resolves by the (source, constructed) type pair and keys a
///     plain <c>T:</c>-&gt;<c>T:</c> <see cref="BaselineEntry.ForEdge" /> entry — zero baseline-format change
///     from a reference edge — pinned here on the in-memory fast tier, no fixture spec required. And the
///     filter-aware catch reach (GRAMMAR §4.8): an unfiltered-catch violation resolves by its (source, caught)
///     pair and writes the same <c>ForEdge</c> entry as any other catch verb, because the recorded unfiltered
///     sites are evidence, never identity — so the valve reaches the new verb with no <c>--add</c> arm of its own.
/// </remarks>
public sealed class BaselineRunnerAddTests : IDisposable
{
    private const string RuleId = "time/inject-clock";
    private const string CtorRuleId = "data-access/no-new";
    private const string UnfilteredCatchRuleId = "exceptions/no-unfiltered-catch";

    private const string Source = """
                                  using System;
                                  namespace MyApp.Web;
                                  public class HomeController { public DateTime Stamp() => DateTime.Now; }
                                  """;

    private const string CtorSource = """
                                      namespace MyApp.Data { public class Db {} }
                                      namespace MyApp.Web { public class OrderController { public MyApp.Data.Db Load() => new MyApp.Data.Db(); } }
                                      """;

    // Three handlers catch the same domain error: two unfiltered (the ratchet's two current reds) and one
    // behind a `when` filter, which the verb never counts at all — so the valve's candidate set is the two,
    // and grandfathering one leaves the other red.
    private const string UnfilteredCatchSource = """
                                                 namespace Errors { public class DbError : System.Exception {} }
                                                 namespace App
                                                 {
                                                     public class LegacyHandler { public void Run() { try { } catch (Errors.DbError) { } } }
                                                     public class ImportHandler { public void Run() { try { } catch (Errors.DbError) { } } }
                                                     public class GuardedHandler { public void Run(bool flag) { try { } catch (Errors.DbError) when (flag) { } } }
                                                 }
                                                 """;

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-runner");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void AddEntry_MemberUseViolationByFullNameTarget_AppendsMemberEntryWithPinnedEcho()
    {
        // Arrange — a captured (empty-section) member ratchet and one observed DateTime.Now use.
        ArchitectureModel model = Checker.Model(arch => arch.Rule(RuleId)
            .Migrate(
                "controllers read the ambient clock",
                arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .Baseline("member.json")
            .Because("b"));
        CheckReport report = ArchChecker.Check(model, CompilationFactory.Extract(Source), BaselineIndex.Empty);
        string path = Path.Combine(_temp.Path, "member.json");
        File.WriteAllText(path, BaselineComposer.Compose(RuleId));

        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        var request = new BaselineRequest(
            null, null, false, false, true, RuleId, "INC-1234",
            "MyApp.Web.HomeController", "System.DateTime.Now", null, _temp.Path, false);

        // Act — the MemberUse violation reaches the added-entry echo with a null Target slot.
        int exit = runner.AddEntry(request, report, _temp.Path);

        // Assert — the pinned echo renders the member display, and the entry keys the P: member ID.
        exit.ShouldBe(0);
        var echo = output.ToString();
        echo.ShouldContain(
            "time/inject-clock: added 1 grandfathered entry — MyApp.Web.HomeController -> System.DateTime.Now (because: INC-1234).");
        echo.ShouldContain("wrote member.json");

        string written = File.ReadAllText(path)
            .NormalizedLines();
        written.ShouldContain(
            "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"P:System.DateTime.Now\", \"because\": \"INC-1234\" }");
        // Composer as oracle: the whole file is the canonical composition of exactly that one entry.
        written.ShouldBe(BaselineComposer.Compose(
            RuleId,
            BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "P:System.DateTime.Now")
                .WithBecause("INC-1234")));
    }

    [Fact]
    public void AddEntry_ConstructionViolationByFullNameTarget_AppendsEdgeEntryWithPinnedEcho()
    {
        // Arrange — a captured (empty-section) construction ratchet and one observed `new MyApp.Data.Db()`.
        ArchitectureModel model = Checker.Model(arch => arch.Rule(CtorRuleId)
            .Migrate(
                "controllers `new` the data layer directly",
                arch.Namespace("MyApp.Web.*").MustNotConstruct(arch.Namespace("MyApp.Data.*")))
            .Baseline("ctor.json")
            .Because("b"));
        CheckReport report = ArchChecker.Check(model, CompilationFactory.Extract(CtorSource), BaselineIndex.Empty);
        string path = Path.Combine(_temp.Path, "ctor.json");
        File.WriteAllText(path, BaselineComposer.Compose(CtorRuleId));

        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        var request = new BaselineRequest(
            null, null, false, false, true, CtorRuleId, "INC-9",
            "MyApp.Web.OrderController", "MyApp.Data.Db", null, _temp.Path, false);

        // Act — the Construction violation reaches the added-entry echo; the constructed type rides the Target slot.
        int exit = runner.AddEntry(request, report, _temp.Path);

        // Assert — the echo renders Source -> Constructed via the shared full-name form, and the entry keys a
        // plain T:->T: ForEdge (identical to a reference edge — zero baseline-format change, GRAMMAR §4.3).
        exit.ShouldBe(0);
        var echo = output.ToString();
        echo.ShouldContain(
            "data-access/no-new: added 1 grandfathered entry — MyApp.Web.OrderController -> MyApp.Data.Db (because: INC-9).");
        echo.ShouldContain("wrote ctor.json");

        string written = File.ReadAllText(path)
            .NormalizedLines();
        written.ShouldContain(
            "        { \"source\": \"T:MyApp.Web.OrderController\", \"target\": \"T:MyApp.Data.Db\", \"because\": \"INC-9\" }");
        // Composer as oracle: the whole file is the canonical composition of exactly that one edge entry.
        written.ShouldBe(BaselineComposer.Compose(
            CtorRuleId,
            BaselineEntry.ForEdge("T:MyApp.Web.OrderController", "T:MyApp.Data.Db")
                .WithBecause("INC-9")));
    }

    [Fact]
    public void AddEntry_UnfilteredCatchViolation_AppendsEdgeEntryAndLeavesTheBystanderRed()
    {
        // Arrange — a captured (empty-section) ratchet over the filter-aware catch verb, and a codebase with two
        // unfiltered catches of the banned type plus one behind a `when` filter.
        ArchitectureModel model = Checker.Model(arch => arch.Rule(UnfilteredCatchRuleId)
            .Migrate(
                "legacy handlers wrap their work in an unfiltered broad catch",
                arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
            .Baseline("catch.json")
            .Because("b"));
        CodebaseModel codebase = CompilationFactory.Extract(UnfilteredCatchSource);
        CheckReport report = ArchChecker.Check(model, codebase, BaselineIndex.Empty);
        report.Single()
            .CatchPairs()
            .ShouldBe(["App.ImportHandler -> Errors.DbError", "App.LegacyHandler -> Errors.DbError"], true);
        string path = Path.Combine(_temp.Path, "catch.json");
        File.WriteAllText(path, BaselineComposer.Compose(UnfilteredCatchRuleId));

        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        var request = new BaselineRequest(
            null, null, false, false, true, UnfilteredCatchRuleId, "INC-77",
            "App.LegacyHandler", "Errors.DbError", null, _temp.Path, false);

        // Act — the valve resolves the unfiltered-catch violation by its (source, caught) type pair.
        int exit = runner.AddEntry(request, report, _temp.Path);

        // Assert — the echo and the appended entry are a plain T:->T: ForEdge, indistinguishable from the
        // sibling catch verb's: the new verb reads a different fact but keys the same identity.
        exit.ShouldBe(0);
        output.ToString()
            .ShouldContain(
                "exceptions/no-unfiltered-catch: added 1 grandfathered entry — App.LegacyHandler -> Errors.DbError (because: INC-77).");
        File.ReadAllText(path)
            .NormalizedLines()
            .ShouldBe(BaselineComposer.Compose(
                UnfilteredCatchRuleId,
                BaselineEntry.ForEdge("T:App.LegacyHandler", "T:Errors.DbError")
                    .WithBecause("INC-77")));

        // …and the ratchet holds on the bytes the valve wrote: re-checking against the file grandfathers the
        // added edge and leaves ImportHandler's identical-looking catch — a distinct identity — red.
        RuleResult ratcheted = ArchChecker.Check(model, codebase, BaselineStore.LoadForModel(model, _temp.Path))
            .Single();
        ratcheted.ShouldHaveFailed();
        ratcheted.CatchPairs()
            .ShouldBe(["App.ImportHandler -> Errors.DbError"]);
        ratcheted.ShouldHaveGrandfathered(1);
    }
}

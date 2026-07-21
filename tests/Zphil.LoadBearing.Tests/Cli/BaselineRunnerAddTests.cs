using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     The <c>baseline --add</c> runner path (<see cref="BaselineRunner.AddEntry" />) over an in-memory
///     fast-tier check — no workspace, unlike the fixture e2e. Pins the member regression: a
///     <see cref="ViolationKind.MemberUse" /> violation resolved by a full-name <c>--target</c> carries a
///     null <c>Target</c> slot, so the added-entry echo must render through the shared full-name form
///     (<c>Source -&gt; member display</c>, GRAMMAR §4.5) and the appended entry must key the member's
///     <c>P:</c> symbol ID. Also the construction reach (GRAMMAR §4.5, §4.3): a
///     <see cref="ViolationKind.Construction" /> resolves by the (source, constructed) type pair and keys a
///     plain <c>T:</c>-&gt;<c>T:</c> <see cref="BaselineEntry.ForEdge" /> entry — zero baseline-format change
///     from a reference edge — pinned here on the in-memory fast tier, no fixture spec required.
/// </summary>
public sealed class BaselineRunnerAddTests : IDisposable
{
    private const string RuleId = "time/inject-clock";
    private const string CtorRuleId = "data-access/no-new";

    private const string Source = """
                                  using System;
                                  namespace MyApp.Web;
                                  public class HomeController { public DateTime Stamp() => DateTime.Now; }
                                  """;

    private const string CtorSource = """
                                      namespace MyApp.Data { public class Db {} }
                                      namespace MyApp.Web { public class OrderController { public MyApp.Data.Db Load() => new MyApp.Data.Db(); } }
                                      """;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "loadbearing-baseline-runner-tests", Guid.NewGuid().ToString("N"));

    public BaselineRunnerAddTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void AddEntry_MemberUseViolationByFullNameTarget_AppendsMemberEntryWithPinnedEcho()
    {
        // Arrange — a captured (empty-section) member ratchet and one observed DateTime.Now use.
        ArchitectureModel model = ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule(RuleId)
            .Migrate(
                "controllers read the ambient clock",
                arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .Baseline("member.json")
            .Because("b")));
        CheckReport report = ArchChecker.Check(model, CompilationFactory.Extract(Source), BaselineIndex.Empty);
        string path = Path.Combine(_dir, "member.json");
        File.WriteAllText(path, Compose([]));

        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        var request = new BaselineRequest(
            null, null, false, false, true, RuleId, "INC-1234",
            "MyApp.Web.HomeController", "System.DateTime.Now", null, _dir);

        // Act — the MemberUse violation reaches the added-entry echo with a null Target slot.
        int exit = runner.AddEntry(request, report, _dir);

        // Assert — the pinned echo renders the member display, and the entry keys the P: member ID.
        exit.ShouldBe(0);
        var echo = output.ToString();
        echo.ShouldContain(
            "time/inject-clock: added 1 grandfathered entry — MyApp.Web.HomeController -> System.DateTime.Now (because: INC-1234).");
        echo.ShouldContain("wrote member.json");

        string written = File.ReadAllText(path).Replace("\r\n", "\n");
        written.ShouldContain(
            "        { \"source\": \"T:MyApp.Web.HomeController\", \"target\": \"P:System.DateTime.Now\", \"because\": \"INC-1234\" }");
        // Composer as oracle: the whole file is the canonical composition of exactly that one entry.
        written.ShouldBe(Compose(
            [BaselineEntry.ForEdge("T:MyApp.Web.HomeController", "P:System.DateTime.Now").WithBecause("INC-1234")]));
    }

    [Fact]
    public void AddEntry_ConstructionViolationByFullNameTarget_AppendsEdgeEntryWithPinnedEcho()
    {
        // Arrange — a captured (empty-section) construction ratchet and one observed `new MyApp.Data.Db()`.
        ArchitectureModel model = ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule(CtorRuleId)
            .Migrate(
                "controllers `new` the data layer directly",
                arch.Namespace("MyApp.Web.*").MustNotConstruct(arch.Namespace("MyApp.Data.*")))
            .Baseline("ctor.json")
            .Because("b")));
        CheckReport report = ArchChecker.Check(model, CompilationFactory.Extract(CtorSource), BaselineIndex.Empty);
        string path = Path.Combine(_dir, "ctor.json");
        File.WriteAllText(path, ComposeCtor([]));

        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        var request = new BaselineRequest(
            null, null, false, false, true, CtorRuleId, "INC-9",
            "MyApp.Web.OrderController", "MyApp.Data.Db", null, _dir);

        // Act — the Construction violation reaches the added-entry echo; the constructed type rides the Target slot.
        int exit = runner.AddEntry(request, report, _dir);

        // Assert — the echo renders Source -> Constructed via the shared full-name form, and the entry keys a
        // plain T:->T: ForEdge (identical to a reference edge — zero baseline-format change, GRAMMAR §4.3).
        exit.ShouldBe(0);
        var echo = output.ToString();
        echo.ShouldContain(
            "data-access/no-new: added 1 grandfathered entry — MyApp.Web.OrderController -> MyApp.Data.Db (because: INC-9).");
        echo.ShouldContain("wrote ctor.json");

        string written = File.ReadAllText(path).Replace("\r\n", "\n");
        written.ShouldContain(
            "        { \"source\": \"T:MyApp.Web.OrderController\", \"target\": \"T:MyApp.Data.Db\", \"because\": \"INC-9\" }");
        // Composer as oracle: the whole file is the canonical composition of exactly that one edge entry.
        written.ShouldBe(ComposeCtor(
            [BaselineEntry.ForEdge("T:MyApp.Web.OrderController", "T:MyApp.Data.Db").WithBecause("INC-9")]));
    }

    private static string Compose(BaselineEntry[] entries)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal)
        {
            [RuleId] = entries
        };
        return BaselineFormat.ComposeFile(rules);
    }

    private static string ComposeCtor(BaselineEntry[] entries)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal)
        {
            [CtorRuleId] = entries
        };
        return BaselineFormat.ComposeFile(rules);
    }
}
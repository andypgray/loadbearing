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
///     <c>P:</c> symbol ID.
/// </summary>
public sealed class BaselineRunnerAddTests : IDisposable
{
    private const string RuleId = "time/inject-clock";

    private const string Source = """
                                  using System;
                                  namespace MyApp.Web;
                                  public class HomeController { public DateTime Stamp() => DateTime.Now; }
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

    private static string Compose(BaselineEntry[] entries)
    {
        var rules = new Dictionary<string, IReadOnlyCollection<BaselineEntry>>(StringComparer.Ordinal)
        {
            [RuleId] = entries
        };
        return BaselineFormat.ComposeFile(rules);
    }
}
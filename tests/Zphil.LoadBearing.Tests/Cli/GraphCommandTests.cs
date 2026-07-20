using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> against the real MyApp fixture solution — the pre-spec codebase survey.
///     Spec-free (no <c>--spec</c>), read-only (no temp copy), always exits 0. The human survey and the
///     <c>--json</c> document (schemaVersion 1) are both pinned by goldens. The fixture truth behind the
///     numbers: 3 projects; Domain→Web = 2 observed type-pairs; Web→Legacy.Billing = 3; Web→System.Data = 2;
///     Web→System.Threading = 2 (HomeController's Task and Task`1 return forms, Phase 14).
/// </summary>
[Collection("Serial")]
public sealed class GraphCommandTests
{
    private const string ExpectedHuman =
        """
        Codebase survey: MyApp.sln

        Projects (3):
          MyApp.Domain — 5 types; references: MyApp.Web
          MyApp.Legacy.Billing — 4 types; references: (none)
          MyApp.Web — 10 types; references: MyApp.Legacy.Billing

        Observed project references (distinct type pairs):
          MyApp.Domain -> MyApp.Web: 2
          MyApp.Web -> MyApp.Legacy.Billing: 3

        Namespaces:
          MyApp.Domain: MyApp.Domain (5)
          MyApp.Legacy.Billing: MyApp.Legacy.Billing (4)
          MyApp.Web: MyApp.Web (10)

        External references (by namespace root):
          MyApp.Legacy.Billing -> System: 2
          MyApp.Web -> System: 2
          MyApp.Web -> System.Data: 2
          MyApp.Web -> System.Text: 1
          MyApp.Web -> System.Threading: 2
        """;

    [Fact]
    public async Task Graph_MyAppFixture_PrintsSurveyAndExitsZero()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution);

        // Assert
        result.Exit.ShouldBe(0);
        Normalize(result.Out).ShouldBe(Normalize(ExpectedHuman));
    }

    [Fact]
    public async Task Graph_MyAppFixtureJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json");

        // Assert
        result.Exit.ShouldBe(0);
        Normalize(result.Out).ShouldBe(Normalize(Golden()));
    }

    private static string Golden()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cli", "Golden", "graph.json"));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}
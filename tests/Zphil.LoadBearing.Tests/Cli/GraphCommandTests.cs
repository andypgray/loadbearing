using System.Text.Json;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Cli.Pipeline;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> against the real MyApp fixture solution — the pre-spec codebase survey.
///     Spec-free (no <c>--spec</c>), read-only (no temp copy), always exits 0. The human survey and the
///     <c>--json</c> document (schemaVersion 1) are both pinned by goldens. The fixture truth behind the
///     numbers: 3 projects; Domain→Web = 2 observed type-pairs; Web→Legacy.Billing = 3; Web→System.Data = 2;
///     Web→System.Threading = 2 (HomeController's Task and Task`1 return forms); Web→Microsoft.Extensions = 2
///     (ServiceWiring's IServiceCollection parameter and the AddSingleton/AddScoped/AddTransient extensions);
///     Domain→System = 3 (OrderRuleViolation's Exception base, OrderApproval's InvalidOperationException throw
///     and RetryPolicy's `when`-filtered caught Exception — a filter never suppresses the reference edge)
///     and Web→System = 4 (ReportEndpoint's and ReportPublisher's caught Exception among them; a rethrowing
///     catch mints its reference edge like any other).
///     <para>
///         The narrowing rows below pin the two knobs and the one automatic behaviour. <c>--overview</c>
///         coarsens the grain (namespace inventories elided, everything else intact); <c>--projects</c>
///         narrows the subject, keeping every edge that touches the scope in either direction — which is
///         why <c>graph-scoped.json</c> names MyApp.Domain and MyApp.Legacy.Billing in its edges and
///         neither in its roster. A filter matching nothing refuses rather than surveying an empty
///         solution, and a fitter carrying the caller's response budget walks the ladder the runner offers,
///         degrading the grain instead of cutting the document.
///     </para>
/// </summary>
[Collection("Serial")]
public sealed class GraphCommandTests
{
    private const string ExpectedHuman =
        """
        Codebase survey: MyApp.sln

        Projects (3):
          MyApp.Domain — 8 types; references: MyApp.Web
          MyApp.Legacy.Billing — 4 types; references: (none)
          MyApp.Web — 18 types; references: MyApp.Legacy.Billing

        Observed project references (distinct type pairs):
          MyApp.Domain -> MyApp.Web: 2
          MyApp.Web -> MyApp.Legacy.Billing: 3

        Types declared by more than one project:
          (none)

        Type names a referenced assembly also supplies:
          (none)

        Namespaces:
          MyApp.Domain: MyApp.Domain (8)
          MyApp.Legacy.Billing: MyApp.Legacy.Billing (4)
          MyApp.Web: MyApp.Web (18)

        External references (by namespace root):
          MyApp.Domain -> System: 3
          MyApp.Legacy.Billing -> System: 2
          MyApp.Web -> Microsoft.Extensions: 2
          MyApp.Web -> System: 4
          MyApp.Web -> System.Data: 2
          MyApp.Web -> System.Text: 1
          MyApp.Web -> System.Threading: 2
        """;

    // Both knobs at once: the scope stamp the runner writes ahead of the survey, and the elision line the
    // formatter writes in place of the namespace inventory. Every section is still here — a survey whose
    // shape changes with its grain would make two runs incomparable.
    private const string ExpectedScopedOverviewHuman =
        """
        Scoped to 1 of 3 projects matching 'MyApp.We*'; references in both directions are kept, so an edge below can name a project outside the scope.

        Codebase survey: MyApp.sln

        Projects (1):
          MyApp.Web — 18 types; references: MyApp.Legacy.Billing

        Observed project references (distinct type pairs):
          MyApp.Domain -> MyApp.Web: 2
          MyApp.Web -> MyApp.Legacy.Billing: 3

        Types declared by more than one project:
          (none)

        Type names a referenced assembly also supplies:
          (none)

        Namespaces:
          (elided at overview grain — rerun without --overview for the per-project namespace inventory)

        External references (by namespace root):
          MyApp.Web -> Microsoft.Extensions: 2
          MyApp.Web -> System: 4
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
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe(ExpectedHuman.NormalizedTrimmed());
    }

    [Fact]
    public async Task Graph_MyAppFixtureJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json");

        // Assert
        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("graph.json");
    }

    [Fact]
    public async Task Graph_MyAppFixtureOverviewJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json", "--overview");

        // Assert — the whole survey at coarser grain: every project, edge and external row still here, each
        // project's namespace inventory gone, and the grain stamped so a reader knows which document this is.
        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("graph-overview.json");
    }

    [Fact]
    public async Task Graph_MyAppFixtureScopedJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json", "--projects", "MyApp.Web");

        // Assert — one project in the roster, both of its edges (inbound from MyApp.Domain included), and the
        // filter recorded in projectsScope so the document says what it covers.
        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("graph-scoped.json");
    }

    [Fact]
    public async Task Graph_OverviewAndProjectsTogether_CoarsensTheGrainAndNarrowsTheSubject()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", CliRunner.MyAppSolution, "--overview", "--projects", "MyApp.We*");

        // Assert — the knobs compose: grain and scope are independent, and neither swallows the other.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe(ExpectedScopedOverviewHuman.NormalizedTrimmed());
    }

    [Fact]
    public async Task Graph_OverviewAndProjectsTogetherAsJson_StampsBothAndElidesNamespaces()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "graph", CliRunner.MyAppSolution, "--json", "--overview", "--projects", "MyApp.We*");

        // Assert
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement root = document.RootElement;

        root.GetProperty("grain")
            .GetString()
            .ShouldBe("overview");
        root.GetProperty("projectsScope")
            .EnumerateArray()
            .Select(glob => glob.GetString())
            .ShouldBe(["MyApp.We*"]);
        root.GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetProperty("name")
                .GetString())
            .ShouldBe(["MyApp.Web"]);

        List<string> projectFields = root.GetProperty("projects")
            .EnumerateArray()
            .SelectMany(project => project.EnumerateObject()
                .Select(field => field.Name))
            .Distinct()
            .ToList();
        projectFields.ShouldNotContain("namespaces");
    }

    [Fact]
    public async Task Graph_ProjectsMatchingNothing_RefusesAndListsTheAvailableProjects()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--projects", "MyApp.Api");

        // Assert — the refusal can only be made after extraction, because the inventory it offers is the
        // extraction's own output; surveying nothing instead would look like an empty solution.
        result.ShouldRefuseWith(
            "No project matched 'MyApp.Api'. Available projects:",
            "MyApp.Domain",
            "MyApp.Legacy.Billing",
            "MyApp.Web");
        result.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task Graph_MyAppFixtureSkeletonJson_MatchesGolden()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", CliRunner.MyAppSolution, "--json", "--skeleton");

        // Assert — the structural spine: every project and edge still here, the namespace inventories gone as
        // at overview grain, and the external rows replaced by their count so the document cannot be read as
        // a codebase with no external dependencies.
        result.ShouldSucceed();
        result.Out.ShouldMatchGolden("graph-skeleton.json");
    }

    [Fact]
    public async Task Graph_JsonDocumentOverTheBudget_DegradesOneRungAtATime()
    {
        // Arrange — a budget the full document overruns but the overview fits, so the ladder must stop at the
        // first rung that fits rather than dropping to its coarsest.
        var degraded = new StringWriter();
        var overview = new StringWriter();
        int overviewLength = await LengthAt(DocumentGrain.Overview);

        // Act
        await Runner(degraded, FixedResponseBudget.Fitter(overviewLength))
            .RunAsync(Request(), Ct);
        await Runner(overview)
            .RunAsync(Request(DocumentGrain.Overview), Ct);

        // Assert — parseable (nothing was truncated), stamped, and byte-identical both to what --overview
        // writes here and to the pinned --overview document, which is what keeps the degraded answer honest
        // about which document a reader is holding.
        using JsonDocument document = JsonDocument.Parse(degraded.ToString());
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("overview");
        degraded.ToString()
            .ShouldBe(overview.ToString());
        degraded.ToString()
            .ShouldMatchGolden("graph-overview.json");
    }

    [Fact]
    public async Task Graph_JsonDocumentOverTheBudgetAtOverviewGrain_KeepsDegradingToSkeleton()
    {
        // Arrange — the regression this ladder exists for. A budget below even the overview's size used to
        // stop after one step and hand the truncator an over-budget document to cut mid-array; measured on a
        // 34-project solution, the full survey was ~147k characters and its overview still ~82k.
        var degraded = new StringWriter();
        var skeleton = new StringWriter();

        // Act
        await Runner(degraded, FixedResponseBudget.Fitter(500))
            .RunAsync(Request(), Ct);
        await Runner(skeleton)
            .RunAsync(Request(DocumentGrain.Skeleton), Ct);

        // Assert — it walked past overview to the last rung, and the answer is still a whole document.
        using JsonDocument document = JsonDocument.Parse(degraded.ToString());
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("skeleton");
        degraded.ToString()
            .ShouldBe(skeleton.ToString());
    }

    [Fact]
    public async Task Graph_ExplicitOverviewOverTheBudget_StillDegrades()
    {
        // Arrange — a caller naming a grain is declaring a floor on detail, not opting out of their own
        // transport's budget. This was the exact hole: the old guard skipped degrading whenever the caller
        // had asked for overview, which is what an MCP client does the moment a survey is large.
        var degraded = new StringWriter();

        // Act
        await Runner(degraded, FixedResponseBudget.Fitter(500))
            .RunAsync(Request(DocumentGrain.Overview), Ct);

        // Assert
        using JsonDocument document = JsonDocument.Parse(degraded.ToString());
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("skeleton");
    }

    [Fact]
    public async Task Graph_JsonDocumentInsideTheBudget_StaysAtFullGrain()
    {
        // Arrange — a budget the full survey fits inside.
        var withBudget = new StringWriter();
        var withoutBudget = new StringWriter();

        // Act
        await Runner(withBudget, FixedResponseBudget.Fitter(100_000))
            .RunAsync(Request(), Ct);
        await Runner(withoutBudget)
            .RunAsync(Request(), Ct);

        // Assert — the budget is a ceiling, not a mode: under it, nothing about the document moves.
        using JsonDocument document = JsonDocument.Parse(withBudget.ToString());
        document.RootElement.TryGetProperty("grain", out _)
            .ShouldBeFalse();
        withBudget.ToString()
            .ShouldBe(withoutBudget.ToString());
    }

    [Fact]
    public async Task Graph_SkeletonGrain_ReportsTheElidedExternalRowsAsACount()
    {
        // Arrange — an elided section must not read as an empty one. The fixture has external edges, so a
        // reader of the skeleton document has to be able to tell "not rendered here" from "none found".
        var skeleton = new StringWriter();
        var full = new StringWriter();

        // Act
        await Runner(skeleton)
            .RunAsync(Request(DocumentGrain.Skeleton), Ct);
        await Runner(full)
            .RunAsync(Request(), Ct);

        // Assert
        using JsonDocument skeletonDocument = JsonDocument.Parse(skeleton.ToString());
        using JsonDocument fullDocument = JsonDocument.Parse(full.ToString());
        int elided = fullDocument.RootElement.GetProperty("externalEdges")
            .GetArrayLength();

        elided.ShouldBeGreaterThan(0);
        skeletonDocument.RootElement.TryGetProperty("externalEdges", out _)
            .ShouldBeFalse();
        skeletonDocument.RootElement.GetProperty("externalEdgeCount")
            .GetInt32()
            .ShouldBe(elided);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000)]
    [InlineData(50_000)]
    public async Task Graph_DegradedDocument_SurvivesTheTruncatorThatRunsAfterIt(int headroom)
    {
        // Arrange — the two stages composed, which is the gap that let a cut survey ship. The runner was
        // pinned in isolation and the truncator was pinned in isolation, so nothing noticed that the runner
        // deliberately emitted an over-budget document and the filter behind it then cut that very document
        // at the same number. Budgets are taken from the skeleton's own size upward, because that is the
        // range where the ladder can deliver a whole answer and therefore must.
        int budget = await LengthAt(DocumentGrain.Skeleton) + headroom;
        var output = new StringWriter();

        // Act — the runner degrades against the budget, then the truncator sees what it produced.
        await Runner(output, FixedResponseBudget.Fitter(budget))
            .RunAsync(Request(), Ct);
        string document = output.ToString()
            .TrimEnd('\r', '\n');
        string afterTruncation = ResponseTruncator.TruncateIfNeeded(document, ArchToolNames.Graph, budget);

        // Assert — the truncator is a no-op here, and what the caller holds is a whole document.
        afterTruncation.ShouldBe(document);
        afterTruncation.ShouldNotContain("RESPONSE TRUNCATED");
        Should.NotThrow(() => JsonDocument.Parse(afterTruncation)
            .Dispose());
    }

    [Fact]
    public async Task Graph_BudgetBelowEvenTheSkeleton_TruncatesAndNamesTheSubjectKnob()
    {
        // Arrange — the ladder's floor, pinned so it stays a known limit rather than a surprise. Below the
        // coarsest grain there is no rung left, so the backstop fires like it does for any other response.
        int belowSkeleton = await LengthAt(DocumentGrain.Skeleton) - 1;
        var output = new StringWriter();

        // Act
        await Runner(output, FixedResponseBudget.Fitter(belowSkeleton))
            .RunAsync(Request(), Ct);
        string document = output.ToString()
            .TrimEnd('\r', '\n');
        string afterTruncation = ResponseTruncator.TruncateIfNeeded(document, ArchToolNames.Graph, belowSkeleton);

        // Assert — cut, and the hint names the one knob still worth reaching for. Naming a grain here would
        // send a reader back down a ladder the survey has already walked to the bottom of.
        afterTruncation.ShouldContain("RESPONSE TRUNCATED");
        afterTruncation.ShouldContain("Narrow the subject");
        afterTruncation.ShouldContain("projects:");
        afterTruncation.ShouldNotContain("overview:");
    }

    // The runner directly, because the response budget has no CLI spelling: it belongs to a caller whose
    // transport has one, and a terminal does not. No fitter is the CLI's own default — the requested grain,
    // never degraded — which is what makes the un-budgeted rows below the control for the budgeted ones.
    private static GraphRunner Runner(TextWriter output, IResponseFitter? fitter = null)
    {
        return new GraphRunner(output, TextWriter.Null, WarmWorkspacePool.Source, fitter: fitter);
    }

    // The composed document's length, not the writer's: the runner measures the document against the budget
    // before Render appends a line terminator, so counting that terminator here would shift every budget
    // these tests derive by the width of one newline — and on Windows that is two characters, not one.
    private static async Task<int> LengthAt(DocumentGrain grain)
    {
        var output = new StringWriter();
        await Runner(output)
            .RunAsync(Request(grain), Ct);
        return output.ToString()
            .TrimEnd('\r', '\n')
            .Length;
    }

    private static GraphRequest Request(DocumentGrain grain = DocumentGrain.Full)
    {
        string workingDirectory = Path.GetDirectoryName(Path.GetFullPath(CliRunner.MyAppSolution))!;
        return new GraphRequest(
            CliRunner.MyAppSolution, true, workingDirectory, false, null, false, grain, null);
    }
}

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
///     <c>check</c>'s grain ladder, driven through the runner against the real MyApp fixture and the violated
///     spec — the behaviour with no CLI spelling, because a response budget belongs to a caller whose
///     transport has one and a terminal does not.
/// </summary>
/// <remarks>
///     <para>
///         The claim is the same one <see cref="GraphCommandTests" /> makes for the survey, and it is worth
///         restating because it is what a client cannot check for itself: an over-budget report comes back
///         <em>whole at a coarser grain</em>, byte-identical to what that grain's own flag writes, rather
///         than cut at a line boundary into JSON nothing can parse. A degraded report is still a verdict over
///         every rule the run selected — grain is never scope.
///     </para>
///     <para>
///         The fixture's rungs are 28.2k / 21.7k / 14.2k / 6.2k characters — overview and skeleton a modest
///         77% and 50% of full, because the MyApp solution has 44 violations over 47 sites. That ratio is an
///         artefact of the fixture, not of the design: sites are the one array with no ceiling, so on the
///         legacy migration this exists for the compression is unbounded. What these rows pin is which rung
///         comes back and that it is whole — not how much it saved.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class CheckCommandTests
{
    [Fact]
    public async Task Check_JsonDocumentOverTheBudget_DegradesOneRungAtATime()
    {
        // Arrange — a budget the full report overruns but the overview fits, so the ladder must stop at the
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
            .ShouldMatchGolden("violated-check-overview.json");
    }

    [Fact]
    public async Task Check_JsonDocumentOverTheBudgetAtOverviewGrain_KeepsDegradingToSkeleton()
    {
        // Arrange — one step is not enough. Sites are the first bulk but not the only one: a spec with many
        // rules over a codebase with many violations overruns again at overview, and stopping there would
        // hand the truncator exactly the document the ladder exists to avoid producing. The budget is the
        // skeleton's own size rather than a round number, because a round number stops naming this rung the
        // moment a coarser one is added below it.
        var degraded = new StringWriter();
        var skeleton = new StringWriter();
        int skeletonLength = await LengthAt(DocumentGrain.Skeleton);

        // Act
        await Runner(degraded, FixedResponseBudget.Fitter(skeletonLength))
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
    public async Task Check_ExplicitOverviewOverTheBudget_StillDegrades()
    {
        // Arrange — a caller naming a grain is declaring a floor on detail, not opting out of their own
        // transport's budget. Which is what an MCP client does the moment a report is large.
        var degraded = new StringWriter();
        int skeletonLength = await LengthAt(DocumentGrain.Skeleton);

        // Act
        await Runner(degraded, FixedResponseBudget.Fitter(skeletonLength))
            .RunAsync(Request(DocumentGrain.Overview), Ct);

        // Assert
        using JsonDocument document = JsonDocument.Parse(degraded.ToString());
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("skeleton");
    }

    [Fact]
    public async Task Check_JsonDocumentInsideTheBudget_StaysAtFullGrain()
    {
        // Arrange — a budget the full report fits inside.
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
    public async Task Check_SkeletonGrain_KeepsEveryRuleWithItsVerdictAndProse()
    {
        // Arrange — why skeleton is the last rung that still answers on its own. Prose scales with the rule
        // count, which is authored and small; violations and sites scale with the codebase, which is what
        // overruns a channel. Dropping the prose takes this fixture's 14.2k to 6.2k — 22% of the full
        // document, an id and a number per rule — which is a menu for the next call, not a verdict: index's
        // job, one rung further down, for the reader whose alternative was a cut report.
        var full = new StringWriter();
        var skeleton = new StringWriter();

        // Act
        await Runner(full)
            .RunAsync(Request(), Ct);
        await Runner(skeleton)
            .RunAsync(Request(DocumentGrain.Skeleton), Ct);

        // Assert
        using JsonDocument fullDocument = JsonDocument.Parse(full.ToString());
        using JsonDocument skeletonDocument = JsonDocument.Parse(skeleton.ToString());

        RuleIds(skeletonDocument)
            .ShouldBe(RuleIds(fullDocument));
        foreach (JsonElement rule in skeletonDocument.RootElement.GetProperty("rules")
                     .EnumerateArray())
        {
            rule.GetProperty("status")
                .GetString()
                .ShouldNotBeNullOrEmpty();
            rule.GetProperty("because")
                .GetString()
                .ShouldNotBeNullOrEmpty();
        }

        // And the counts never move with the grain: a coarser report still totals the same solution.
        skeletonDocument.RootElement.GetProperty("summary")
            .GetRawText()
            .ShouldBe(
                fullDocument.RootElement.GetProperty("summary")
                    .GetRawText());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000)]
    [InlineData(50_000)]
    public async Task Check_DegradedDocument_SurvivesTheTruncatorThatRunsAfterIt(int headroom)
    {
        // Arrange — the two stages composed, which is the gap a runner pinned in isolation cannot see: the
        // runner degrades against a budget and the filter behind it then cuts at the same number. Budgets are
        // taken from the skeleton's own size upward, because that is the range where the ladder can deliver a
        // whole answer and therefore must.
        int budget = await LengthAt(DocumentGrain.Skeleton) + headroom;
        var output = new StringWriter();

        // Act — the runner degrades against the budget, then the truncator sees what it produced.
        await Runner(output, FixedResponseBudget.Fitter(budget))
            .RunAsync(Request(), Ct);
        string document = output.ToString()
            .TrimEnd('\r', '\n');
        string afterTruncation = ResponseTruncator.TruncateIfNeeded(document, ArchToolNames.Check, budget);

        // Assert — the truncator is a no-op here, and what the caller holds is a whole document.
        afterTruncation.ShouldBe(document);
        afterTruncation.ShouldNotContain("RESPONSE TRUNCATED");
        Should.NotThrow(() => JsonDocument.Parse(afterTruncation)
            .Dispose());
    }

    [Fact]
    public async Task Check_BudgetBelowEvenTheSkeleton_DegradesToIndexRatherThanTruncating()
    {
        // Arrange — the case the field found and this rung answers. A Spectre.Console bed's report was 67,909
        // characters against a 62,500 budget, so the ladder ran out and the report came back cut; the agent
        // that received it ran whole-document CLI checks instead and never called the tool again. One
        // character below the skeleton is the same condition in miniature.
        int belowSkeleton = await LengthAt(DocumentGrain.Skeleton) - 1;
        var output = new StringWriter();

        // Act
        await Runner(output, FixedResponseBudget.Fitter(belowSkeleton))
            .RunAsync(Request(), Ct);
        string document = output.ToString()
            .TrimEnd('\r', '\n');
        string afterTruncation = ResponseTruncator.TruncateIfNeeded(document, ArchToolNames.Check, belowSkeleton);

        // Assert — a whole report at the floor rung, untouched by the truncator behind it.
        afterTruncation.ShouldBe(document);
        afterTruncation.ShouldNotContain("RESPONSE TRUNCATED");
        using JsonDocument parsed = JsonDocument.Parse(afterTruncation);
        parsed.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("index");
    }

    [Fact]
    public async Task Check_BudgetBelowEvenTheIndex_TruncatesAndNamesTheSubjectKnob()
    {
        // Arrange — the ladder's floor, pinned so it stays a known limit rather than a surprise. Below the
        // coarsest grain there is no rung left, so the backstop fires like it does for any other response.
        // What reaches here is a budget too small for a list of rule IDs, not a large spec.
        int belowIndex = await LengthAt(DocumentGrain.Index) - 1;
        var output = new StringWriter();

        // Act
        await Runner(output, FixedResponseBudget.Fitter(belowIndex))
            .RunAsync(Request(), Ct);
        string document = output.ToString()
            .TrimEnd('\r', '\n');
        string afterTruncation = ResponseTruncator.TruncateIfNeeded(document, ArchToolNames.Check, belowIndex);

        // Assert — cut, and the hint names the one knob still worth reaching for. Naming a grain here would
        // send a reader back down a ladder the report has already walked to the bottom of.
        afterTruncation.ShouldContain("RESPONSE TRUNCATED");
        afterTruncation.ShouldContain("Narrow the subject");
        afterTruncation.ShouldContain("rules:");
        afterTruncation.ShouldNotContain("overview:");
    }

    [Fact]
    public async Task Check_IndexGrain_NamesEveryRuleWithItsVerdictAndDropsOnlyTheProse()
    {
        // Arrange — what the floor rung is for. Skeleton keeps the prose because that is what makes it a
        // verdict a reader can act on; index is not competing with skeleton for that reader, it is competing
        // with a cut document. What survives is the menu the next call needs: the ids rules globs match, and
        // the ids arch_explain expands one at a time — prose and all.
        var index = new StringWriter();
        var skeleton = new StringWriter();

        // Act
        await Runner(index)
            .RunAsync(Request(DocumentGrain.Index), Ct);
        await Runner(skeleton)
            .RunAsync(Request(DocumentGrain.Skeleton), Ct);

        // Assert
        using JsonDocument indexDocument = JsonDocument.Parse(index.ToString());
        using JsonDocument skeletonDocument = JsonDocument.Parse(skeleton.ToString());

        RuleIds(indexDocument)
            .ShouldBe(RuleIds(skeletonDocument));
        foreach (JsonElement rule in indexDocument.RootElement.GetProperty("rules")
                     .EnumerateArray())
        {
            rule.GetProperty("status")
                .GetString()
                .ShouldNotBeNullOrEmpty();
            rule.GetProperty("violationCount")
                .GetInt32()
                .ShouldBeGreaterThanOrEqualTo(0);
            rule.TryGetProperty("sentence", out _)
                .ShouldBeFalse();
            rule.TryGetProperty("because", out _)
                .ShouldBeFalse();
            rule.TryGetProperty("fix", out _)
                .ShouldBeFalse();
        }

        // And the counts never move with the grain, at this rung as at every other.
        indexDocument.RootElement.GetProperty("summary")
            .GetRawText()
            .ShouldBe(
                skeletonDocument.RootElement.GetProperty("summary")
                    .GetRawText());
    }

    [Fact]
    public async Task Check_ExplicitIndexOverTheBudget_DegradesNothingFurther()
    {
        // Arrange — the floor is the floor from both directions: a caller who names it and still overruns
        // gets it anyway, rather than the ladder spinning or the runner inventing a coarser answer.
        var output = new StringWriter();

        // Act
        await Runner(output, FixedResponseBudget.Fitter(1))
            .RunAsync(Request(DocumentGrain.Index), Ct);

        // Assert
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        document.RootElement.GetProperty("grain")
            .GetString()
            .ShouldBe("index");
    }

    // The runner directly, because the response budget has no CLI spelling. No fitter is the CLI's own
    // default — the requested grain, never degraded — which is what makes the un-budgeted rows the control.
    private static CheckRunner Runner(TextWriter output, IResponseFitter? fitter = null)
    {
        return new CheckRunner(output, TextWriter.Null, WarmWorkspacePool.Source, fitter: fitter);
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

    private static IReadOnlyList<string?> RuleIds(JsonDocument document)
    {
        return document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Select(rule => rule.GetProperty("id")
                .GetString())
            .ToList();
    }

    private static CheckRequest Request(DocumentGrain grain = DocumentGrain.Full)
    {
        string workingDirectory = SolutionPaths.SolutionDirectoryOf(CliRunner.MyAppSolution);
        return CheckRequests.For(CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll, workingDirectory)
            with
            {
                Json = true, Grain = grain
            };
    }
}

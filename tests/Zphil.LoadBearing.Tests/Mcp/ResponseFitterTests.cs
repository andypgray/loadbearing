using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli;
using Zphil.LoadBearing.Cli.Mcp.Pipeline;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Mcp.TestDoubles;

namespace Zphil.LoadBearing.Tests.Mcp;

/// <summary>
///     The two fitters and the budget behind them — the machinery that decides which rung of a runner's
///     coarsening ladder a caller actually gets, held here in isolation from any runner so the rungs can be
///     plain strings of known length.
/// </summary>
/// <remarks>
///     <para>
///         Laziness is a correctness property here, not a performance note: composing a rung costs a full
///         serialization of the document, so a fitter that materializes the ladder pays for coarser answers
///         nobody reads. The rows below count what was pulled rather than trusting the shape of the code.
///     </para>
///     <para>
///         So is the per-call budget read. A <see cref="ResponseBudget" /> that snapshotted its
///         <c>IEnvironment</c> at construction would pass every other row in this file and pin the first
///         call's cap for the life of the server — the failure <c>CliMcpParityTests.HarnessG</c> catches
///         end to end, and the one this file catches directly.
///     </para>
/// </remarks>
public sealed class ResponseFitterTests
{
    [Fact]
    public void FirstRung_LadderOfSeveral_TakesTheFinestAndComposesNothingElse()
    {
        // Arrange — the CLI's fitter: the caller named a grain and a terminal has no budget to overrun.
        List<string> composed = [];

        // Act
        string fitted = ResponseFitter.FirstRung.Fit(Ladder(composed, "full", "overview", "skeleton"));

        // Assert — the requested grain, and the coarser rungs were never paid for.
        fitted.ShouldBe("full");
        composed.ShouldBe(["full"]);
    }

    [Fact]
    public void Budgeted_FirstRungFits_TakesItAndComposesNothingElse()
    {
        // Arrange — a budget the finest rung sits inside, which is every call on a codebase small enough.
        List<string> composed = [];
        IResponseFitter fitter = FixedResponseBudget.Fitter(10);

        // Act
        string fitted = fitter.Fit(Ladder(composed, "12345678", "1234", "12"));

        // Assert — a budget is a ceiling, not a mode.
        fitted.ShouldBe("12345678");
        composed.ShouldBe(["12345678"]);
    }

    [Fact]
    public void Budgeted_FinerRungsOverrun_TakesTheFirstThatFitsAndStopsThere()
    {
        // Arrange — the whole point: answer whole at a coarser grain rather than cut at the finer one.
        List<string> composed = [];
        IResponseFitter fitter = FixedResponseBudget.Fitter(5);

        // Act
        string fitted = fitter.Fit(Ladder(composed, "12345678", "1234", "12"));

        // Assert — it walked past the rung that overran and stopped at the first that fit; the coarsest was
        // never composed, so a document that fits at overview never pays for its skeleton.
        fitted.ShouldBe("1234");
        composed.ShouldBe(["12345678", "1234"]);
    }

    [Fact]
    public void Budgeted_BudgetExactlyTheRungLength_TakesThatRung()
    {
        // Arrange — the boundary the truncator uses too: at the cap, nothing is cut.
        IResponseFitter fitter = FixedResponseBudget.Fitter(4);

        // Act
        string fitted = fitter.Fit(Ladder([], "12345678", "1234", "12"));

        // Assert
        fitted.ShouldBe("1234");
    }

    [Fact]
    public void Budgeted_EvenTheCoarsestRungOverruns_ReturnsTheCoarsestAnyway()
    {
        // Arrange — the ladder's floor. Below the coarsest grain there is no rung left, so the answer goes
        // out over budget and the truncator behind it cuts like it does for any other response: a ladder
        // with a last rung, not a guarantee.
        List<string> composed = [];
        IResponseFitter fitter = FixedResponseBudget.Fitter(1);

        // Act
        string fitted = fitter.Fit(Ladder(composed, "12345678", "1234", "12"));

        // Assert — the whole ladder was walked, and what comes back is the smallest whole document there is.
        fitted.ShouldBe("12");
        composed.ShouldBe(["12345678", "1234", "12"]);
    }

    [Fact]
    public void Budgeted_BudgetMovesBetweenCalls_EachCallFitsAgainstTheCurrentOne()
    {
        // Arrange — the fitter is a singleton over a whole server lifetime, and a client can move its
        // budget between tool calls. Reading the cap once would pin the first call's answer forever.
        MovableResponseBudget budget = new(10);
        IResponseFitter fitter = new BudgetedResponseFitter(budget);

        // Act
        string wide = fitter.Fit(Ladder([], "12345678", "1234", "12"));
        budget.MoveTo(5);
        string narrow = fitter.Fit(Ladder([], "12345678", "1234", "12"));

        // Assert
        wide.ShouldBe("12345678");
        narrow.ShouldBe("1234");
    }

    [Fact]
    public void ResponseBudget_EnvironmentChangesBetweenCalls_ReportsTheCurrentCap()
    {
        // Arrange — the same property one layer down, at the seam that actually reads process state.
        FakeEnvironment environment = new();
        IResponseBudget budget = new ResponseBudget(environment);

        // Act & Assert — unset first, so the default is not mistaken for a cached value.
        budget.MaxChars()
            .ShouldBe(62_500);

        environment.SetVariable(LoadBearingEnvVars.MaxMcpOutputTokens, "1000");
        budget.MaxChars()
            .ShouldBe(2_500);

        environment.SetVariable(LoadBearingEnvVars.MaxMcpOutputTokens, "4000");
        budget.MaxChars()
            .ShouldBe(10_000);
    }

    // A ladder that records what it was asked to compose, so the rows above can assert on how far the
    // fitter walked rather than inferring it. Yields lazily, exactly as a runner's own ladder does.
    private static IEnumerable<string> Ladder(List<string> composed, params string[] rungs)
    {
        foreach (string rung in rungs)
        {
            composed.Add(rung);
            yield return rung;
        }
    }

    // A budget whose value can be moved between calls, which is what a client declaring a smaller window
    // mid-session looks like from in here.
    private sealed class MovableResponseBudget(int initial) : IResponseBudget
    {
        private int _maxChars = initial;

        public int MaxChars()
        {
            return _maxChars;
        }

        public void MoveTo(int maxChars)
        {
            _maxChars = maxChars;
        }
    }
}

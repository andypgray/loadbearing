using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <see cref="ContextRunner" />'s one refusal: a <c>path</c> that arrived as a JSON array written as
///     text. Every other <c>arch_context</c> behaviour is pinned beside the verbs it shares a run with
///     (<see cref="FilteredSolutionE2ETests" />, <see cref="NarrowingGateOrderE2ETests" />,
///     <see cref="PartialLoadWorkspaceE2ETests" />); what is pinned here is the branch those cannot reach,
///     because it fires before any of the machinery they exercise exists.
/// </summary>
public sealed class ContextRunnerTests
{
    [Fact]
    public async Task Context_PathAsAStringifiedArray_RefusesOnTheShapeAndEchoesTheOneValue()
    {
        // The defect this closes: brackets resolve to a directory no card covers, so the verb used to answer
        // "no architecture scope covers …" and exit 0 — a well-formed proven negative to a question nobody
        // asked, on the exact call an agent is told to make before editing unfamiliar territory.
        UserErrorException refusal = await ShouldRefuse("""["src/Foo"]""");

        refusal.Message.ShouldBe(
            "Cannot find architecture scope for '[\"src/Foo\"]'. That is a JSON array written as text; "
            + "pass one path: 'src/Foo'.");
    }

    [Fact]
    public async Task Context_SeveralPathsInOneArray_NamesTheShapeAndEchoesNothing()
    {
        // No echo: an array of several holds no single path this verb could have taken, and picking one of
        // them for the caller would be a guess dressed as advice.
        UserErrorException refusal = await ShouldRefuse("""["src/Foo","src/Bar"]""");

        refusal.Message.ShouldBe(
            "Cannot find architecture scope for '[\"src/Foo\",\"src/Bar\"]'. That is a JSON array written as "
            + "text; pass one path.");
    }

    [Fact]
    public async Task Context_PathAsAStringifiedArray_RefusesAheadOfTheLoad()
    {
        // The solution named here does not exist, so reaching the workspace at all would raise a different
        // error entirely — which is what makes this the pin on ORDER. It has to hold: the incomplete-model
        // caveat block is written to the body before any answer, and a caveat above a refusal qualifies
        // nothing.
        var output = new StringWriter();
        var runner = new ContextRunner(output);
        string absentSolution = Path.Combine(Path.GetTempPath(), "no-such-solution.slnx");
        var request = new ContextRequest("""["src/Foo"]""", absentSolution, null, Path.GetTempPath());

        var refusal = await Should.ThrowAsync<UserErrorException>(() => runner.RunAsync(request, Ct));

        refusal.Message.ShouldStartWith("Cannot find architecture scope for");
        output.ToString()
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Context_ADirectoryNamedLikeAnArray_IsRefusedRatherThanRewritten()
    {
        // The reader is never a repair, so a caller whose tree really does hold a bracketed directory name
        // gets their own text quoted back and one round trip to disambiguate — not a silent rewrite into a
        // path they did not ask about.
        UserErrorException refusal = await ShouldRefuse("[weird]");

        refusal.Message.ShouldBe(
            "Cannot find architecture scope for '[weird]'. That is a JSON array written as text; "
            + "pass one path: 'weird'.");
    }

    // The writer rides inline because no case going through here reads it — the order pin above keeps its
    // own, being the one test that asserts on the output channel.
    private static async Task<UserErrorException> ShouldRefuse(string path)
    {
        var runner = new ContextRunner(new StringWriter());
        return await Should.ThrowAsync<UserErrorException>(() => runner.RunAsync(Request(path), Ct));
    }

    // The solution and spec are never read — every case here throws before the load — so the shortest
    // request that compiles is the honest one.
    private static ContextRequest Request(string path)
    {
        return new ContextRequest(path, CliRunner.MyAppSolution, CliRunner.CleanSpecDll, Path.GetTempPath());
    }
}

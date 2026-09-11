using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>
///     <see cref="CheckRunner" />'s one pre-flight refusal: a <c>--diff-base</c> that arrived as a JSON
///     array written as text. What it says about the run is pinned in
///     <see cref="CheckCommandE2ETests" /> beside the <c>--rules</c> twin; what is pinned here is
///     <em>when</em> it fires, which no run that reaches the workspace can show.
/// </summary>
public sealed class CheckRunnerTests
{
    [Fact]
    public async Task Check_DiffBaseAsAStringifiedArray_RefusesAheadOfTheLoad()
    {
        // The solution named here does not exist, so reaching the workspace at all would raise a different
        // error entirely — which is what makes this the pin on ORDER, and why it drives the runner rather
        // than the CLI (solution discovery runs at the entry, above every runner). It has to hold for the
        // reason arch_context's twin does: the narrowing and unsupported-project stamps go to the human
        // channel before the diff is ever resolved, and a preamble above a refusal qualifies nothing.
        // Ref validation itself cannot move up here — git runs in the solution directory the load resolves
        // — so the shape half is exactly the half that can be judged this early, and this is the pin that
        // keeps it there.
        var output = new StringWriter();
        var runner = new CheckRunner(output, TextWriter.Null);
        string absentSolution = Path.Combine(Path.GetTempPath(), "no-such-solution.slnx");
        var request = new CheckRequest(
            absentSolution, null, false, false, """["HEAD"]""", Path.GetTempPath(), false, null, false, null,
            null, DocumentGrain.Full);

        var refusal = await Should.ThrowAsync<UserErrorException>(() => runner.RunAsync(request, Ct));

        refusal.Message.ShouldBe(
            "Cannot resolve changed files since '[\"HEAD\"]'. That is a JSON array written as text; "
            + "pass one git ref: 'HEAD'.");
        output.ToString()
            .ShouldBeEmpty();
    }
}

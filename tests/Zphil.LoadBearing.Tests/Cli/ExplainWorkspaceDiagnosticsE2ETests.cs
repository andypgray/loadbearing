using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.Verbs;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Cli;

/// <summary>The workspace-diagnostics contract on <c>explain</c> — the verb that renders but never gates.</summary>
/// <remarks>
///     <para>
///         <c>explain</c> took no error writer at all, so a load failure on the workspace it opens for
///         resolution was discarded: not gated, not rendered, not recorded anywhere. It renders the composed
///         diagnostics now and still answers, because the rule it dumps comes from the spec and not from the
///         codebase — a project that failed to load cannot make the answer wrong, only leave the reader
///         unaware that anything failed.
///     </para>
///     <para>
///         Driven through <see cref="ExplainRunner" /> with a
///         <see cref="DiagnosticInjectingSolutionSource" /> over this repository's own solution and its
///         arch-spec csproj: the workspace path needs a solution whose spec resolves <em>through</em> that
///         workspace, and this is the only such pair the suite has. No fixture solution declares a spec
///         project, so the convention and csproj branches refuse there before a workspace is ever consulted,
///         and a built-DLL <c>--spec</c> takes the fast path instead. The genuinely-partial load lives in
///         <see cref="PartialLoadWorkspaceE2ETests" />; what this class owns is which channel the failures
///         reach.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class ExplainWorkspaceDiagnosticsE2ETests
{
    private const string LoadDiagnostic = "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Explain_WorkspacePathWithLoadFailure_RendersTheDiagnosticsAndStillAnswers()
    {
        CliResult result = await RunAsync(
            new ExplainRequest("cli/no-stdout", RepoRoot.Solution, RepoRoot.ArchSpecCsproj, RepoRoot.Directory));

        result.ShouldSucceed("cli/no-stdout"); // the answer still arrives — it was never at risk
        result.Err.ShouldContain($"warning: {LoadDiagnostic}"); // and the load failure is visible beside it
        result.Err.ShouldContain("MSBuild for this run:"); // composed like every other verb's
        result.Err.ShouldNotContain("error: the model is incomplete"); // nothing gates: the answer is spec-derived
    }

    [Fact]
    public async Task Explain_DllFastPath_OpensNoWorkspaceSoThereIsNothingToRender()
    {
        // Silent by construction rather than by a condition: the fast path returns before the render, so this
        // source — which would have injected a load failure — is never acquired at all.
        CliResult result = await RunAsync(
            new ExplainRequest(
                "layering/domain-independent", CliRunner.MyAppSolution, CliRunner.ViolatedSpecDll,
                Path.GetDirectoryName(CliRunner.MyAppSolution)!));

        result.ShouldSucceed("layering/domain-independent (enforce)");
        result.Err.ShouldBeEmpty();
    }

    private static Task<CliResult> RunAsync(ExplainRequest request)
    {
        var source = new DiagnosticInjectingSolutionSource([LoadDiagnostic]);

        return CliResult.CapturedAsync((output, error) => new ExplainRunner(output, error, source).RunAsync(request, Ct));
    }
}

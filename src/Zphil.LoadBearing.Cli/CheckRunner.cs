using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>check</c> pipeline: build a <see cref="CodebaseSource" /> (cache hit or cold workspace) → run
///     the shared <see cref="CheckPipeline" /> (baselines, extraction, ratcheted check) → render (human
///     or JSON) → exit code (0 clean / 1 red violations; grandfathered Migrate violations do not fail
///     the run). Expected failures surface as <see cref="UserErrorException" />; the top-level handler
///     maps them to exit 2. Output/error writers are injected so the in-process e2e tests can capture them,
///     and the <see cref="IEnvironment" /> seam supplies the cache-root override.
/// </summary>
internal sealed class CheckRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
{
    private readonly IEnvironment environment = environment ?? new SystemEnvironment();
    private readonly ISolutionSource solutionSource = source ?? new ColdSolutionSource();

    /// <summary>The cache path the last run took. Internal test observable; never printed.</summary>
    internal CodebaseSourceOutcome? LastOutcome { get; private set; }

    /// <summary>The projects the last run re-extracted from a workspace. Internal test observable; never printed.</summary>
    internal IReadOnlySet<string> LastReExtractedProjects { get; private set; } = new HashSet<string>();

    public async Task<int> RunAsync(CheckRequest request, CancellationToken ct)
    {
        using var source = await CodebaseSource.CreateWithSpecAsync(
            solutionSource, environment, request.Solution, request.Spec, request.WorkingDirectory, request.NoCache, ct);

        CheckReport report = await CheckPipeline.ExecuteAsync(source, request.DiffBase, ct);
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
        Render(
            request, report, source.SolutionDirectory, Path.GetFileName(source.SolutionPath),
            Path.GetFileName(source.Resolution.DllPath), source.Diagnostics);

        return report.HasViolations ? 1 : 0;
    }

    private void Render(
        CheckRequest request, CheckReport report, string solutionDirectory, string solutionName, string specAssembly,
        IReadOnlyList<string> diagnostics)
    {
        if (request.Json)
        {
            // --json purity: only the JSON document reaches stdout; diagnostics go to stderr and ride
            // inside the document's workspaceDiagnostics array.
            foreach (string diagnostic in diagnostics) error.WriteLine(diagnostic);
            JsonReportRenderer.Render(output, report, solutionDirectory, solutionName, specAssembly, request.DiffBase, diagnostics);
        }
        else
        {
            foreach (string diagnostic in diagnostics) error.WriteLine($"warning: {diagnostic}");
            HumanReportRenderer.Render(output, report, solutionDirectory);
        }
    }
}
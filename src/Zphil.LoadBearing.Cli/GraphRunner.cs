using Zphil.LoadBearing.Cli.Mcp.Infrastructure;
using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>graph</c> pipeline: build a spec-less <see cref="CodebaseSource" /> (cache hit or cold
///     workspace) → extract the whole codebase (no project exclusions, no spec) → summarize → render the
///     survey (human or JSON). Deliberately spec-free: the survey is a property of the codebase, and derive
///     runs before any spec exists — so unlike the other verbs there is no spec resolution here, only the
///     shared solution discovery and workspace acquisition through an <see cref="ISolutionSource" /> (cold by
///     default), fronted by the persisted extraction cache. Reports rather than gates: it exits 0 on success
///     regardless of what the codebase contains; a discovery/workspace failure surfaces as a
///     <see cref="UserErrorException" /> the top-level handler maps to exit 2. Output/error writers are
///     injected so the in-process e2e tests can capture them, and the <see cref="IEnvironment" /> seam
///     supplies the cache-root override.
/// </summary>
internal sealed class GraphRunner(
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

    public async Task<int> RunAsync(GraphRequest request, CancellationToken ct)
    {
        using var source = await CodebaseSource.CreateSpeclessAsync(
            solutionSource, environment, request.Solution, request.WorkingDirectory, request.NoCache, ct);

        CodebaseModel codebase = await source.ExtractAsync(null, ct);
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string solutionName = Path.GetFileName(source.SolutionPath);

        if (request.Json)
        {
            // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr.
            foreach (string diagnostic in source.Diagnostics) error.WriteLine(diagnostic);
            GraphJsonRenderer.Render(output, summary, solutionName);
        }
        else
        {
            foreach (string diagnostic in source.Diagnostics) error.WriteLine($"warning: {diagnostic}");
            foreach (string line in GraphFormatter.Lines(summary, solutionName)) output.WriteLine(line);
        }

        return 0;
    }
}
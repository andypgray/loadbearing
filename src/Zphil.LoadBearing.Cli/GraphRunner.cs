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
///     default), fronted by the persisted extraction cache. What the codebase <em>contains</em> never gates
///     — the survey exits 0 however alarming the graph is. What gates is the survey not being of the
///     codebase: a project that failed to load is missing from the map entirely, so the run refuses before
///     extraction on <c>check</c>'s terms, opt-out <see cref="GraphRequest.AllowWorkspaceDiagnostics" />
///     (<see cref="IncompleteModelGate" />). That refusal, like a discovery/workspace failure, surfaces as a
///     <see cref="UserErrorException" /> — exit 2 on the CLI, an error result on MCP — which is why it is
///     thrown rather than written: this is the one verb with no spec to resolve, so it is the first command
///     a stranger runs and the first that must explain itself on whichever surface asked. Output/error
///     writers are injected so the in-process e2e tests can capture them, and the
///     <see cref="IEnvironment" /> seam supplies the cache-root override.
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

        // Refuse before extraction, not after: a survey of a partial model is a wrong map, not a smaller one.
        // The refusal carries the diagnostics in its own text rather than leaving them to the stderr render
        // below, so the MCP surface — which discards this error writer — gets the same actionable message.
        // A cache hit refuses identically: diagnostics persist into the extraction cache and replay with it.
        bool modelIncomplete = IncompleteModelGate.IsIncomplete(source.Diagnostics);
        if (IncompleteModelGate.Gates(source.Diagnostics, request.AllowWorkspaceDiagnostics))
            throw new UserErrorException(IncompleteModelGate.GraphRefusal(source.Diagnostics));

        CodebaseModel codebase = await source.ExtractAsync([], ct); // spec-less: the survey excludes nothing
        LastOutcome = source.Outcome;
        LastReExtractedProjects = source.ReExtractedProjects;
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string solutionName = Path.GetFileName(source.SolutionPath);

        // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr. Composed
        // once for both, so the survey document carries the MSBuild-selection note the refusal above already
        // carries. The gate read source.Diagnostics, not this list.
        var renderedDiagnostics = WorkspaceDiagnosticsRenderer.Compose(source.Diagnostics);
        WorkspaceDiagnosticsRenderer.Render(error, renderedDiagnostics, request.Json);

        if (request.Json)
            GraphJsonRenderer.Render(output, summary, solutionName, renderedDiagnostics, modelIncomplete);
        else
            foreach (string line in GraphFormatter.Lines(summary, solutionName))
                output.WriteLine(line);

        return 0;
    }
}

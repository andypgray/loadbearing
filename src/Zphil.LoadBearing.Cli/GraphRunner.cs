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
///     default), fronted by the persisted extraction cache.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it refuses.</b> What the codebase <em>contains</em> never gates — the survey exits 0
///         however alarming the graph is. What gates is the survey not being of the codebase: a project that
///         failed to load is missing from the map entirely, so the run refuses before extraction on
///         <c>check</c>'s terms, opt-out <see cref="GraphRequest.AllowWorkspaceDiagnostics" />
///         (<see cref="IncompleteModelGate" />). That refusal, like a discovery/workspace failure, surfaces
///         as a <see cref="UserErrorException" /> — exit 2 on the CLI, an error result on MCP — which is why
///         it is thrown rather than written: this is the one verb with no spec to resolve, so it is the
///         first command a stranger runs and the first that must explain itself on whichever surface asked.
///     </para>
///     <para>
///         Two knobs narrow what a caller reads, and they are independent: <see cref="GraphRequest.Projects" />
///         narrows the <em>subject</em> (which projects the survey is of), while
///         <see cref="GraphRequest.Grain" /> coarsens the <em>grain</em> (how much detail each one gets).
///         A filter matching no project refuses with the available names rather than surveying nothing.
///     </para>
///     <para>
///         Output/error writers are injected so the in-process e2e tests can capture them, the
///         <see cref="IEnvironment" /> seam supplies the cache-root override, and the
///         <see cref="IResponseFitter" /> decides which rung of the grain ladder a caller with a response
///         budget actually gets (default: the grain they asked for).
///     </para>
/// </remarks>
internal sealed class GraphRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null,
    IResponseFitter? fitter = null) : CacheWiredRunner(source, environment, fitter)
{
    public async Task<int> RunAsync(GraphRequest request, CancellationToken ct)
    {
        // This run's human channel. --json owns stdout, where the survey document is the only thing written,
        // so under it the stamp below goes nowhere.
        TextWriter human = request.Json ? TextWriter.Null : output;

        using var source = await CodebaseSource.CreateSpeclessAsync(
            SolutionSource, Environment, request.Solution, request.WorkingDirectory, request.NoCache, ct);

        // Refuse before extraction, not after: a survey of a partial model is a wrong map, not a smaller one.
        // The refusal carries the diagnostics in its own text rather than leaving them to the stderr render
        // below, so the MCP surface — which discards this error writer — gets the same actionable message.
        // A cache hit refuses identically: diagnostics persist into the extraction cache and replay with it.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        if (diagnostics.Gates(request.AllowWorkspaceDiagnostics))
            throw new UserErrorException(IncompleteModelGate.GraphRefusal(diagnostics));

        CodebaseModel codebase = await source.ExtractAsync([], ct); // spec-less: the survey excludes nothing
        RecordCacheOutcome(source);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string solutionName = source.SolutionName;

        // Scope, then refuse, then render. The refusal fires here rather than at parse time because the
        // inventory it lists IS the extraction's output: nothing before this point knows the solution's
        // project names, so a filter that matches nothing cannot be caught any earlier.
        IReadOnlyList<string> projectGlobs = GlobList.Parse(request.Projects);
        GraphSummary scoped = GraphSummarizer.Scope(summary, projectGlobs);
        if (projectGlobs.Count > 0 && scoped.Projects.Count == 0)
            throw new UserErrorException(UnmatchedProjectsMessage(projectGlobs, summary));

        // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr. Composed
        // once for both, so the survey document carries the MSBuild-selection note the refusal above already
        // carries.
        IReadOnlyList<string> renderedDiagnostics = diagnostics.Rendered;
        WorkspaceDiagnosticsRenderer.Render(error, renderedDiagnostics, request.Json);

        // The narrowing stamp is human-channel only, where the document carries the same fact in
        // uncheckedProjects.
        NarrowingNotices.Stamp(human, source, NarrowedUniverseNotice.GraphStamp);

        if (request.Json)
            WriteJson(
                request, scoped, source.SolutionDirectory, solutionName, renderedDiagnostics, diagnostics,
                projectGlobs);
        else
            WriteHuman(request, summary, scoped, solutionName, projectGlobs);

        return 0;
    }

    // The JSON survey, degraded rather than cut. The runner offers every grain from the requested floor down
    // to the coarsest as a lazy ladder and the fitter picks; a caller whose transport has a response budget
    // gets the first rung that fits, re-composed from the summary already in hand — no second extraction, and
    // byte-identical to what that grain's own flag would have written, so the two surfaces cannot drift.
    // Degrading coarsens the grain and never touches the scope: the answer stays about the codebase the
    // caller asked about, and every rung carries the same narrowing.
    private void WriteJson(
        GraphRequest request, GraphSummary scoped, string solutionDirectory, string solutionName,
        IReadOnlyList<string> renderedDiagnostics, WorkspaceDiagnostics diagnostics,
        IReadOnlyList<string> projectGlobs)
    {
        IEnumerable<string> ladder = DocumentGrains.Ladder(request.Grain, Compose);
        string document = Fitter.Fit(ladder);
        output.WriteLine(document);
        return;

        string Compose(DocumentGrain at)
        {
            return GraphJsonRenderer.Document(
                scoped, solutionDirectory, solutionName, renderedDiagnostics, diagnostics, at, projectGlobs);
        }
    }

    // The human survey. The scope stamp is written here rather than by the formatter so an unscoped run is
    // byte-identical to what it always was, and so the stamp can say what the formatter cannot see: how much
    // of the solution this covers, and why an edge below may name a project the roster does not.
    private void WriteHuman(
        GraphRequest request, GraphSummary summary, GraphSummary scoped, string solutionName,
        IReadOnlyList<string> projectGlobs)
    {
        if (projectGlobs.Count > 0)
        {
            output.WriteLine(ScopeStamp(projectGlobs, summary, scoped));
            output.WriteLine();
        }

        foreach (string line in GraphFormatter.Lines(scoped, solutionName, request.Grain))
            output.WriteLine(line);
    }

    private static string ScopeStamp(IReadOnlyList<string> projectGlobs, GraphSummary summary, GraphSummary scoped)
    {
        return $"Scoped to {scoped.Projects.Count} of {summary.Projects.Count} projects matching "
               + $"'{string.Join(";", projectGlobs)}'; references in both directions are kept, so an edge below "
               + "can name a project outside the scope.";
    }

    // The unmatched-filter refusal, in the shared shape explain's unknown-rule refusal also takes. The
    // roster rides in survey order, not sorted: it is the summary's own order, which is what the rest of
    // the survey prints.
    private static string UnmatchedProjectsMessage(IReadOnlyList<string> projectGlobs, GraphSummary summary)
    {
        return Refusals.NotFoundMessage(
            $"No project matched '{string.Join(";", projectGlobs)}'",
            "Available projects",
            summary.Projects.Select(project => project.Name));
    }
}

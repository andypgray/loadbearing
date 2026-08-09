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
///     <para>
///         Two knobs narrow what a caller reads, and they are independent: <see cref="GraphRequest.Projects" />
///         narrows the <em>subject</em> (which projects the survey is of), while
///         <see cref="GraphRequest.Grain" /> coarsens the <em>grain</em> (how much detail each one gets).
///         A filter matching no project refuses with the available names rather than surveying nothing.
///     </para>
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

        // Scope, then refuse, then render. The refusal fires here rather than at parse time because the
        // inventory it lists IS the extraction's output: nothing before this point knows the solution's
        // project names, so a filter that matches nothing cannot be caught any earlier.
        var projectGlobs = GlobList.Parse(request.Projects);
        GraphSummary scoped = GraphSummarizer.Scope(summary, projectGlobs);
        if (projectGlobs.Count > 0 && scoped.Projects.Count == 0)
            throw new UserErrorException(UnmatchedProjectsMessage(projectGlobs, summary));

        // --json purity: only the JSON document reaches stdout; workspace diagnostics go to stderr. Composed
        // once for both, so the survey document carries the MSBuild-selection note the refusal above already
        // carries. The gate read source.Diagnostics, not this list.
        var renderedDiagnostics = WorkspaceDiagnosticsRenderer.Compose(source.Diagnostics);
        WorkspaceDiagnosticsRenderer.Render(error, renderedDiagnostics, request.Json);

        if (request.Json)
            WriteJson(request, scoped, solutionName, renderedDiagnostics, modelIncomplete, projectGlobs);
        else
            WriteHuman(request, summary, scoped, solutionName, projectGlobs);

        return 0;
    }

    // The JSON survey, degraded rather than cut. A caller whose transport has a response budget declares it,
    // and a document that overruns is re-composed one rung coarser from the summary already in hand — no
    // second extraction, and byte-identical to what that grain's own flag would have written, so the two
    // surfaces cannot drift. Degrading coarsens the grain and never touches the scope: the answer stays about
    // the codebase the caller asked about.
    //
    // It walks the whole ladder rather than stepping once, because one step is not enough on a real solution:
    // on a 34-project codebase the full survey is ~147k characters and the overview it degrades to is still
    // ~82k, over any default budget. Stopping there handed the truncator exactly the document this method
    // exists to avoid producing — a survey cut mid-array, unparseable, which is worse for a reader than a
    // whole answer in less detail. The loop cannot spin: every rung is strictly coarser and Skeleton is last.
    private void WriteJson(
        GraphRequest request, GraphSummary scoped, string solutionName,
        IReadOnlyList<string> renderedDiagnostics, bool modelIncomplete, IReadOnlyList<string> projectGlobs)
    {
        GraphGrain grain = request.Grain;
        string document = Compose(grain);

        while (Overruns(document, request) && grain < GraphGrain.Skeleton)
        {
            grain++;
            document = Compose(grain);
        }

        GraphJsonRenderer.Render(output, document);
        return;

        string Compose(GraphGrain at)
        {
            return GraphJsonRenderer.Document(
                scoped, solutionName, renderedDiagnostics, modelIncomplete, at, projectGlobs);
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

    // No "unless the caller asked for this grain" guard: an explicit --overview that still overruns is the
    // exact case that used to reach the truncator, and the caller asking for overview grain is asking for a
    // floor on detail, not a promise to hand back a document their own transport cannot carry.
    private static bool Overruns(string document, GraphRequest request)
    {
        return request.ResponseBudgetChars is { } budget && document.Length > budget;
    }

    private static string ScopeStamp(IReadOnlyList<string> projectGlobs, GraphSummary summary, GraphSummary scoped)
    {
        return $"Scoped to {scoped.Projects.Count} of {summary.Projects.Count} projects matching "
               + $"'{string.Join(";", projectGlobs)}'; references in both directions are kept, so an edge below "
               + "can name a project outside the scope.";
    }

    // The unmatched-filter refusal, worded like explain's unknown-rule refusal: name what did not match, then
    // list what was available, so the next command is one edit away.
    private static string UnmatchedProjectsMessage(IReadOnlyList<string> projectGlobs, GraphSummary summary)
    {
        var names = summary.Projects.Select(project => project.Name);
        return $"No project matched '{string.Join(";", projectGlobs)}'. Available projects:\n  "
               + string.Join("\n  ", names);
    }
}

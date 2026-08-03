using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>render</c> pipeline: load the model → extract the codebase, but only when the model has
///     quarantined scopes or layers carrying anchored rules → hand both to
///     <see cref="ContextFileComposer" />, which composes every target file's managed block (the root
///     block in the solution directory, each layer's local-rules card and each scope's card in its own
///     directory, merged where they coincide). The composer is shared with the card-drift gate, so this
///     command and the test that proves the committed cards current cannot disagree about what a render
///     produces. Every target is spliced through
///     the byte-level <see cref="ManagedBlockFile" /> adapter and reported as <c>wrote</c>/<c>unchanged</c>
///     with a solution-relative path. <c>--diagram &lt;path&gt;</c> adds a second, independent target: the
///     codebase graph and the law as two fences, composed by <see cref="DiagramComposer" />, on the same
///     wrote/unchanged stream. Render is a mutation, not a gate: it always exits 0 on success;
///     expected failures surface as <see cref="UserErrorException" /> (exit 2). Render never exits 1.
/// </summary>
internal sealed class RenderRunner(TextWriter output, TextWriter error, ISolutionSource? source = null)
{
    private readonly ISolutionSource solutionSource = source ?? new ColdSolutionSource();

    public async Task<int> RunAsync(RenderRequest request, CancellationToken ct)
    {
        ValidateDiagramOptions(request);

        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            solutionSource, request.Solution, request.Spec, request.WorkingDirectory, ct);

        WorkspaceDiagnosticsRenderer.Render(error, workspace.Diagnostics);

        string specName = Path.GetFileNameWithoutExtension(workspace.Resolution.DllPath);
        string solutionDirectory = workspace.SolutionDirectory;

        // Extraction only earns its cost when there is something scoped to place — a quarantined scope, or a
        // layer carrying anchored rules; with nothing scoped the composer gets no codebase and returns the
        // root block alone.
        bool anyQuarantine = workspace.Model.Rules.Any(rule => rule.Posture == Posture.Quarantine);
        CodebaseModel? codebase = anyQuarantine || LayerContextResolver.HasAnchoredLayers(workspace.Model)
            ? await CodebaseExtractor.ExtractFromSolutionAsync(
                workspace.Solution, workspace.Resolution.ExcludeProjectNames, ct)
            : null;

        ContextComposition composition = ContextFileComposer.Compose(
            workspace.Model, codebase, solutionDirectory, specName);

        foreach (string warning in composition.Warnings) error.WriteLine($"warning: {warning}");

        WriteFiles(composition.Files, solutionDirectory);

        if (request.Diagram is { } diagramPath) await WriteDiagramAsync(request, workspace, specName, diagramPath, ct);

        return 0;
    }

    // The scope options are meaningless without a target file, and silently ignoring them would let a
    // mistyped --diagram render the whole solution. Validated before the workspace cost, like baseline's
    // mode check.
    private static void ValidateDiagramOptions(RenderRequest request)
    {
        if (request.Diagram is null && (request.DiagramOnly is not null || request.DiagramExclude is not null))
            throw new UserErrorException("--diagram-only and --diagram-exclude apply only with --diagram <path>.");
    }

    // The diagram target. It runs its own extraction, with no project exclusions, which is the same call
    // `graph` makes: the scoped-card extraction above passes the spec resolution's excluded projects (the
    // spec project plus the private plumbing only it references), and reusing it would draw a diagram that
    // disagrees with the survey it is supposed to be. Both feed one GraphSummarizer, so there is still one
    // summary shape; the second extraction is the cost, and only when --diagram and scoped cards coincide.
    // The law fence beside it costs nothing extra — it is pure over the model already in hand.
    private async Task WriteDiagramAsync(
        RenderRequest request, WorkspaceModel workspace, string specName, string diagramPath, CancellationToken ct)
    {
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, [], ct);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string body = DiagramComposer.Compose(
            summary, Path.GetFileName(workspace.SolutionPath), workspace.Model, specName, DiagramScopeFrom(request));

        WriteOutcome outcome = ManagedBlockFile.Splice(diagramPath, body);
        string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
        output.WriteLine($"{label} {PathFormat.Relative(workspace.SolutionDirectory, diagramPath)}");
    }

    private static DiagramScope DiagramScopeFrom(RenderRequest request)
    {
        return new DiagramScope(Globs(request.DiagramOnly), Globs(request.DiagramExclude));
    }

    // Semicolon-separated globs, the MSBuild list idiom; blanks are dropped so a trailing separator is not
    // a pattern that matches nothing.
    private static IReadOnlyList<string> Globs(string? value)
    {
        return value is null
            ? []
            : value.Split(';').Select(glob => glob.Trim()).Where(glob => glob.Length > 0).ToList();
    }

    // Splices each composed file and reports it on the wrote/unchanged stream with a solution-relative
    // path. The composition arrives root-first, which is the order the report reads in.
    private void WriteFiles(IReadOnlyList<ContextFile> files, string solutionDirectory)
    {
        foreach (ContextFile file in files)
        {
            WriteOutcome outcome = ManagedBlockFile.Splice(file.Path, file.Body);
            string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
            output.WriteLine($"{label} {PathFormat.Relative(solutionDirectory, file.Path)}");
        }
    }
}
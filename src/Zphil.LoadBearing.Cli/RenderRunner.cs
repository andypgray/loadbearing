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
///     wrote/unchanged stream.
///     Render fails closed on an incomplete model like every verb that consumes it — exit 2 after the
///     diagnostics and before the first byte hits disk, opt-out <c>--allow-workspace-diagnostics</c> —
///     because its output is committed context: a card whose project failed to load cannot be placed and
///     would be dropped from the committed files, and <c>--diagram</c> would draw the survey <c>graph</c>
///     refuses. On a complete model it exits 0 on success; expected failures surface as
///     <see cref="UserErrorException" /> (exit 2). Render never exits 1.
/// </summary>
internal sealed class RenderRunner(TextWriter output, TextWriter error, ISolutionSource? source = null)
    : WorkspaceRunner(source)
{
    public async Task<int> RunAsync(RenderRequest request, CancellationToken ct)
    {
        ValidateDiagramOptions(request);

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, request.Solution, request.Spec, request.WorkingDirectory, ct);

        // Composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics.Rendered);

        // Fail closed before the first byte hits disk: the files render writes are committed context, and a
        // partial model composes them wrong — a card whose project failed to load resolves no directory and
        // is dropped, and --diagram draws the very survey graph refuses to print.
        if (diagnostics.Gates(request.AllowWorkspaceDiagnostics))
        {
            error.WriteLine(IncompleteModelGate.RenderMessage);
            return 2;
        }

        string specName = Path.GetFileNameWithoutExtension(source.Resolution.DllPath);
        string solutionDirectory = source.SolutionDirectory;

        // Extraction only earns its cost when there is something scoped to place — a quarantined scope, or a
        // layer carrying anchored rules; with nothing scoped the composer gets no codebase and returns the
        // root block alone.
        CodebaseModel? codebase = ContextFileComposer.HasAnythingToPlace(source.Model)
            ? await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct)
            : null;

        ContextComposition composition = ContextFileComposer.Compose(
            source.Model, codebase, solutionDirectory, specName);

        foreach (string warning in composition.Warnings) error.WriteLine($"warning: {warning}");

        WriteFiles(composition.Files, solutionDirectory);

        if (request.Diagram is { } diagramPath) await WriteDiagramAsync(request, source, specName, diagramPath, ct);

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
        RenderRequest request, CodebaseSource source, string specName, string diagramPath, CancellationToken ct)
    {
        CodebaseModel codebase = await source.ExtractAsync([], ct);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string body = DiagramComposer.Compose(
            summary, Path.GetFileName(source.SolutionPath), source.Model, specName, DiagramScopeFrom(request));

        WriteOutcome outcome = ManagedBlockFile.Splice(diagramPath, body);
        output.WriteLine(WriteReport.Line(outcome, source.SolutionDirectory, diagramPath));
    }

    private static DiagramScope DiagramScopeFrom(RenderRequest request)
    {
        return new DiagramScope(GlobList.Parse(request.DiagramOnly), GlobList.Parse(request.DiagramExclude));
    }

    // Splices each composed file and reports it on the wrote/unchanged stream with a solution-relative
    // path. The composition arrives root-first, which is the order the report reads in.
    private void WriteFiles(IReadOnlyList<ContextFile> files, string solutionDirectory)
    {
        foreach (ContextFile file in files)
        {
            WriteOutcome outcome = ManagedBlockFile.Splice(file.Path, file.Body);
            output.WriteLine(WriteReport.Line(outcome, solutionDirectory, file.Path));
        }
    }
}

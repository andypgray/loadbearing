using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>render</c> pipeline: load the model → compose the root block into
///     <c>&lt;solution-dir&gt;/AGENTS.md</c> → and, when the model has quarantined scopes or layers carrying
///     anchored rules, extract the codebase and place each layer's local-rules card and each scope's
///     card in its directory's <c>AGENTS.md</c>. Content units that land in the same directory merge
///     into that file's one managed block (layer card before quarantine card). Every target is spliced through
///     the byte-level <see cref="ManagedBlockFile" /> adapter and reported as <c>wrote</c>/<c>unchanged</c>
///     with a solution-relative path. <c>--diagram &lt;path&gt;</c> adds a second, independent target: the
///     codebase graph as a Mermaid diagram in its own file's own managed block, reported on the same
///     wrote/unchanged stream. Render is a mutation, not a gate: it always exits 0 on success;
///     expected failures surface as <see cref="UserErrorException" /> (exit 2). Render never exits 1.
/// </summary>
internal sealed class RenderRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(RenderRequest request, CancellationToken ct)
    {
        ValidateDiagramOptions(request);

        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);

        foreach (string diagnostic in workspace.Diagnostics) error.WriteLine($"warning: {diagnostic}");

        string specName = Path.GetFileNameWithoutExtension(workspace.Resolution.DllPath);
        string solutionDirectory = workspace.SolutionDirectory;

        var units = new List<ContentUnit>
        {
            new(solutionDirectory, AgentContextRenderer.RootBlock(workspace.Model, specName), true)
        };

        // Extraction only earns its cost when there is something scoped to place — a quarantined scope, or a
        // layer carrying anchored rules. Layer cards precede quarantine cards, so a directory holding both
        // merges the layer card first into its one managed block.
        bool anyQuarantine = workspace.Model.Rules.Any(rule => rule.Posture == Posture.Quarantine);
        if (anyQuarantine || LayerContextResolver.HasAnchoredLayers(workspace.Model))
            units.AddRange(await ScopedUnitsAsync(workspace, ct));

        WriteGroups(units, specName, solutionDirectory);

        if (request.Diagram is { } diagramPath) await WriteDiagramAsync(request, workspace, diagramPath, ct);

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
    private async Task WriteDiagramAsync(
        RenderRequest request, WorkspaceModel workspace, string diagramPath, CancellationToken ct)
    {
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, [], ct);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string body = GraphDiagramRenderer.Block(
            summary, Path.GetFileName(workspace.SolutionPath), DiagramScopeFrom(request));

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

    // The non-root content units: extract the codebase once, then the layer cards (declaration order)
    // ahead of the quarantine cards (model order). A directory that hosts a layer and a quarantined scope thus
    // receives its layer unit before its quarantine unit, and WriteGroups merges them in that order.
    private async Task<IEnumerable<ContentUnit>> ScopedUnitsAsync(WorkspaceModel workspace, CancellationToken ct)
    {
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(
            workspace.Solution, workspace.Resolution.ExcludeProjectNames, ct);

        var units = new List<ContentUnit>();
        foreach (LayerPlacement placement in LayerContextResolver.Resolve(workspace.Model, codebase))
        {
            if (placement.DirectoryPath is null)
            {
                error.WriteLine($"warning: {placement.SkipReason}");
                continue;
            }

            units.Add(new ContentUnit(
                placement.DirectoryPath, AgentContextRenderer.LayerCard(placement.LayerName, placement.Rules), false));
        }

        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(workspace.Model, codebase))
        {
            if (placement.DirectoryPath is null)
            {
                error.WriteLine($"warning: {placement.SkipReason}");
                continue;
            }

            units.Add(new ContentUnit(
                placement.DirectoryPath, AgentContextRenderer.ScopeCard(placement.ContainmentRule), false));
        }

        return units;
    }

    // Groups content units by target directory (first-seen order, so the root file is reported first),
    // assembles each file's one managed block — one provenance line, then the units in model order —
    // and splices it. A group that includes the root uses the root block's own provenance; a scope-only
    // file gets the provenance line prepended.
    private void WriteGroups(IReadOnlyList<ContentUnit> units, string specName, string solutionDirectory)
    {
        var groups = new List<FileGroup>();
        foreach (ContentUnit unit in units)
        {
            string key = Path.GetFullPath(unit.Directory);
            // Per-OS so two case-spellings of one directory merge into a single AGENTS.md on
            // case-insensitive file systems (Windows/macOS) — Ordinal would splice the file twice, the
            // second clobbering the first — while staying distinct on Linux.
            FileGroup? group = groups.FirstOrDefault(candidate => string.Equals(candidate.Key, key, PathComparison.Comparison));
            if (group is null)
            {
                group = new FileGroup(key, unit.Directory);
                groups.Add(group);
            }

            group.Bodies.Add(unit.Body);
            group.HasRoot |= unit.IsRoot;
        }

        foreach (FileGroup group in groups)
        {
            string body = group.HasRoot
                ? string.Join("\n\n", group.Bodies)
                : AgentContextRenderer.ProvenanceLine(specName) + "\n\n" + string.Join("\n\n", group.Bodies);

            string filePath = Path.Combine(group.Directory, "AGENTS.md");
            WriteOutcome outcome = ManagedBlockFile.Splice(filePath, body);
            string label = outcome == WriteOutcome.Wrote ? "wrote" : "unchanged";
            output.WriteLine($"{label} {PathFormat.Relative(solutionDirectory, filePath)}");
        }
    }

    private sealed record ContentUnit(string Directory, string Body, bool IsRoot);

    private sealed class FileGroup(string key, string directory)
    {
        public string Key { get; } = key;
        public string Directory { get; } = directory;
        public List<string> Bodies { get; } = [];
        public bool HasRoot { get; set; }
    }
}
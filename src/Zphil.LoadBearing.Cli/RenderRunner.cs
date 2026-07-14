using Zphil.LoadBearing.Cli.Rendering;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>render</c> pipeline (R3): load the model → compose the root block into
///     <c>&lt;solution-dir&gt;/AGENTS.md</c> → and, only when the model has frozen scopes, extract the
///     codebase, place each scope's card in its directory's <c>AGENTS.md</c>. Content units that land
///     in the same directory merge into that file's one managed block. Every target is spliced through
///     the byte-level <see cref="ManagedBlockFile" /> adapter and reported as <c>wrote</c>/<c>unchanged</c>
///     with a solution-relative path. Render is a mutation, not a gate: it always exits 0 on success;
///     expected failures surface as <see cref="UserErrorException" /> (exit 2). Render never exits 1.
/// </summary>
internal sealed class RenderRunner(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(RenderRequest request, CancellationToken ct)
    {
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);

        foreach (string diagnostic in workspace.Diagnostics) error.WriteLine($"warning: {diagnostic}");

        string specName = Path.GetFileNameWithoutExtension(workspace.Resolution.DllPath);
        string solutionDirectory = workspace.SolutionDirectory;

        var units = new List<ContentUnit>
        {
            new(solutionDirectory, AgentContextRenderer.RootBlock(workspace.Model, specName), true)
        };

        // Extraction only earns its cost when there are scopes to place.
        if (workspace.Model.Rules.Any(rule => rule.Posture == Posture.Freeze))
            units.AddRange(await ScopeUnitsAsync(workspace, ct));

        WriteGroups(units, specName, solutionDirectory);
        return 0;
    }

    private async Task<IEnumerable<ContentUnit>> ScopeUnitsAsync(WorkspaceModel workspace, CancellationToken ct)
    {
        IReadOnlyCollection<string>? exclude = workspace.Resolution.ExcludeProjectName is { } name ? [name] : null;
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, exclude, ct);

        var scopeUnits = new List<ContentUnit>();
        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(workspace.Model, codebase))
        {
            if (placement.DirectoryPath is null)
            {
                error.WriteLine($"warning: {placement.SkipReason}");
                continue;
            }

            scopeUnits.Add(new ContentUnit(
                placement.DirectoryPath, AgentContextRenderer.ScopeCard(placement.ContainmentRule), false));
        }

        return scopeUnits;
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
            FileGroup? group = groups.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
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
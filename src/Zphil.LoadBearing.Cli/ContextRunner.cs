using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>arch_context</c> pipeline (the MCP <c>arch_context</c> tool's core): load the model → if no
///     frozen scope and no anchored layer exist, emit the pointer line and stop (no extraction —
///     <see cref="RenderRunner" />'s cost gate) → otherwise extract, resolve each layer's and each frozen
///     scope's directory placement, and write the covering cards (layer card(s) before freeze card(s)) for
///     every placement whose resolved directory contains the query path. No placement covers the path ⇒ the
///     same pinned pointer line. Always exits 0 — context is a lookup, never a gate. The card body carries
///     no provenance line (that is a <c>render</c> file-splice concern).
/// </summary>
internal sealed class ContextRunner(TextWriter output, ISolutionSource? source = null)
{
    private readonly ISolutionSource solutionSource = source ?? new ColdSolutionSource();

    public async Task<int> RunAsync(ContextRequest request, CancellationToken ct)
    {
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            solutionSource, request.Solution, request.Spec, request.WorkingDirectory, ct);

        // Nothing scoped to place — no frozen scope and no anchored layer — ⇒ skip the extraction cost
        // and point at the root block.
        bool anyFreeze = workspace.Model.Rules.Any(rule => rule.Posture == Posture.Freeze);
        if (!anyFreeze && !LayerContextResolver.HasAnchoredLayers(workspace.Model))
        {
            output.WriteLine(PointerLine(request.Path));
            return 0;
        }

        IReadOnlyCollection<string>? exclude = workspace.Resolution.ExcludeProjectName is { } name ? [name] : null;
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, exclude, ct);

        string queryFullPath = ResolveQueryPath(request.Path, workspace.SolutionDirectory);

        // Layer local-rules card(s) first, then frozen-scope card(s) — the same order render merges them.
        var cards = new List<string>();
        cards.AddRange(LayerContextResolver.Resolve(workspace.Model, codebase)
            .Where(placement => placement.DirectoryPath is not null && Covers(placement.DirectoryPath, queryFullPath))
            .Select(placement => AgentContextRenderer.LayerCard(placement.LayerName, placement.Rules)));
        cards.AddRange(ScopedContextResolver.Resolve(workspace.Model, codebase)
            .Where(placement => placement.DirectoryPath is not null && Covers(placement.DirectoryPath, queryFullPath))
            .Select(placement => AgentContextRenderer.ScopeCard(placement.ContainmentRule)));

        if (cards.Count == 0)
        {
            output.WriteLine(PointerLine(request.Path));
            return 0;
        }

        WriteCards(cards);
        return 0;
    }

    // Each matching card — layer cards before freeze cards — blank line between cards. The card body is
    // LF-internal; write it line by line so it adopts the output writer's newline (parity with the CLI).
    private void WriteCards(IReadOnlyList<string> cards)
    {
        var first = true;
        foreach (string card in cards)
        {
            if (!first) output.WriteLine();
            first = false;

            foreach (string line in card.Split('\n'))
                output.WriteLine(line);
        }
    }

    // Canonicalize the query path to the same standard as the model's declaration-site paths (both
    // symlink-resolved), so a user/agent path spelled through a symlinked root still matches a scope.
    private static string ResolveQueryPath(string path, string solutionDirectory)
    {
        string full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(solutionDirectory, path));
        return PathCanonicalizer.Resolve(full);
    }

    // True when the scope's resolved directory equals or is an ancestor of the query path — i.e. the
    // directory's full-path segments are a prefix of the query's, compared with the per-OS rule.
    private static bool Covers(string directoryPath, string queryFullPath)
    {
        string[] dirSegments = Segments(Path.GetFullPath(directoryPath));
        string[] querySegments = Segments(queryFullPath);
        if (dirSegments.Length > querySegments.Length) return false;

        for (var i = 0; i < dirSegments.Length; i++)
            if (!string.Equals(dirSegments[i], querySegments[i], PathComparison.Comparison))
                return false;

        return true;
    }

    private static string[] Segments(string fullPath)
    {
        string[] parts = fullPath.Split('/', '\\');
        int end = parts.Length;
        while (end > 0 && parts[end - 1].Length == 0) end--;

        return end == parts.Length ? parts : parts.Take(end).ToArray();
    }

    private static string PointerLine(string path)
    {
        return $"No architecture scope covers '{path}'. Architecture context for this solution lives in the root " +
               "AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.";
    }
}
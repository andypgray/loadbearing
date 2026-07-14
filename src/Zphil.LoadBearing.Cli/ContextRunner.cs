using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Cli;

/// <summary>
///     The <c>arch_context</c> pipeline (the MCP <c>arch_context</c> tool's core): load the model → if no
///     frozen scope exists, emit the pointer line and stop (no extraction — <see cref="RenderRunner" />'s
///     cost gate) → otherwise extract, resolve each frozen scope's directory placement, and write the
///     scope card for every scope whose resolved directory contains the query path. No frozen scope covers
///     the path ⇒ the same pinned pointer line. Always exits 0 — context is a lookup, never a gate. The
///     scope-card body carries no provenance line (that is a <c>render</c> file-splice concern).
/// </summary>
internal sealed class ContextRunner(TextWriter output)
{
    // Mirror PathFormat's per-OS segment comparison: case-insensitive on Windows and macOS, ordinal on Linux.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public async Task<int> RunAsync(ContextRequest request, CancellationToken ct)
    {
        using WorkspaceModel workspace = await ModelPipeline.LoadWithWorkspaceAsync(
            request.Solution, request.Spec, request.WorkingDirectory, ct);

        // No frozen scopes ⇒ nothing scoped to place; skip the extraction cost and point at the root block.
        if (!workspace.Model.Rules.Any(rule => rule.Posture == Posture.Freeze))
        {
            output.WriteLine(PointerLine(request.Path));
            return 0;
        }

        IReadOnlyCollection<string>? exclude = workspace.Resolution.ExcludeProjectName is { } name ? [name] : null;
        CodebaseModel codebase = await CodebaseExtractor.ExtractFromSolutionAsync(workspace.Solution, exclude, ct);

        string queryFullPath = ResolveQueryPath(request.Path, workspace.SolutionDirectory);

        var matching = ScopedContextResolver.Resolve(workspace.Model, codebase)
            .Where(placement => placement.DirectoryPath is not null && Covers(placement.DirectoryPath, queryFullPath))
            .ToList();

        if (matching.Count == 0)
        {
            output.WriteLine(PointerLine(request.Path));
            return 0;
        }

        WriteCards(matching);
        return 0;
    }

    // Each frozen scope's card, in model order, blank line between cards. The card body is LF-internal;
    // write it line by line so it adopts the output writer's newline (parity with the CLI surfaces).
    private void WriteCards(IReadOnlyList<ScopePlacement> matching)
    {
        var first = true;
        foreach (ScopePlacement placement in matching)
        {
            if (!first) output.WriteLine();
            first = false;

            foreach (string line in AgentContextRenderer.ScopeCard(placement.ContainmentRule).Split('\n'))
                output.WriteLine(line);
        }
    }

    private static string ResolveQueryPath(string path, string solutionDirectory)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(solutionDirectory, path));
    }

    // True when the scope's resolved directory equals or is an ancestor of the query path — i.e. the
    // directory's full-path segments are a prefix of the query's, compared with the per-OS rule.
    private static bool Covers(string directoryPath, string queryFullPath)
    {
        string[] dirSegments = Segments(Path.GetFullPath(directoryPath));
        string[] querySegments = Segments(queryFullPath);
        if (dirSegments.Length > querySegments.Length) return false;

        for (var i = 0; i < dirSegments.Length; i++)
            if (!string.Equals(dirSegments[i], querySegments[i], PathComparison))
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
        return $"No frozen scope covers '{path}'. Architecture context for this solution lives in the root " +
               "AGENTS.md managed block; expand any rule with 'loadbearing explain <rule-id>'.";
    }
}
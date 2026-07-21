using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Extracts a <see cref="CodebaseModel" /> — type nodes plus <c>file:line</c> reference edges —
///     from Roslyn compilations. Two entries share one builder core: a fast path over hand-built
///     <see cref="CompilationInput" />s (no MSBuild), and the solution path used against a real
///     <see cref="Solution" /> loaded by <see cref="WorkspaceLoader" />.
/// </summary>
public static class CodebaseExtractor
{
    /// <summary>
    ///     Extracts the model from the given compilations. Every input is declared before any
    ///     reference is walked, so a type referenced across compilations unifies to its declaring
    ///     node by fully-qualified name.
    /// </summary>
    public static CodebaseModel ExtractFromCompilations(IReadOnlyList<CompilationInput> inputs)
    {
        return CodebaseModelBuilder.Build(inputs);
    }

    /// <summary>
    ///     Extracts the model from a loaded solution: C# projects in ordinal name order, each project's
    ///     compilation plus its forward project references (by name), delegated to the shared builder.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="excludeProjects">
    ///     Project names to exclude from the checked universe — used by the CLI to drop the spec
    ///     project when it is itself a member of the target solution. Null (the default) excludes
    ///     nothing, so existing callers are unaffected.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving Roslyn types before
    ///     <c>MSBuildLocator</c> registration in non-test hosts.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CodebaseModel> ExtractFromSolutionAsync(
        Solution solution, IReadOnlyCollection<string>? excludeProjects = null, CancellationToken ct = default)
    {
        IReadOnlyList<CompilationInput> inputs = await CollectInputsAsync(
            solution, p => excludeProjects is null || !excludeProjects.Contains(p.Name), ct);
        return CodebaseModelBuilder.Build(inputs);
    }

    /// <summary>
    ///     Extracts one self-contained <see cref="CodebaseFragment" /> per C# project matched by
    ///     <paramref name="includeProjects" /> (null extracts every C# project), in the same ordinal
    ///     project order and with the same per-project <see cref="CompilationInput" />s the merge-producing
    ///     path uses — so the fragments a workspace-loaded run persists and the fragments a
    ///     cache hit replays go through the identical <see cref="FragmentMerger" />, and the cache cannot
    ///     change results by construction. The extraction cache calls this with <c>null</c> on a miss
    ///     (extract all) and with just the dirty projects on a partial (reuse the clean fragments).
    /// </summary>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving Roslyn types before
    ///     <c>MSBuildLocator</c> registration in non-test hosts, matching <see cref="ExtractFromSolutionAsync" />.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<IReadOnlyList<CodebaseFragment>> ExtractFragmentsAsync(
        Solution solution, IReadOnlyCollection<string>? includeProjects, CancellationToken ct = default)
    {
        IReadOnlyList<CompilationInput> inputs = await CollectInputsAsync(
            solution, p => includeProjects is null || includeProjects.Contains(p.Name), ct);
        return inputs.Select(FragmentExtractor.Extract).ToList();
    }

    // The shared project enumeration behind both entry points: C# projects passing the filter, in ordinal
    // name order (multi-target-framework projects preserve their solution order within a name), each turned
    // into a CompilationInput carrying its forward project-reference names. One enumeration means the
    // extract-all-then-merge cold path, the extract-fragments cache path, and cache hits all order identically.
    private static async Task<List<CompilationInput>> CollectInputsAsync(
        Solution solution, Func<Project, bool> include, CancellationToken ct)
    {
        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(include)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        List<CompilationInput> inputs = [];
        foreach (Project project in projects)
        {
            ct.ThrowIfCancellationRequested();

            Compilation? compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var projectReferences = project.ProjectReferences
                .Select(r => solution.GetProject(r.ProjectId)?.Name)
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            inputs.Add(new CompilationInput(compilation, project.Name, projectReferences));
        }

        return inputs;
    }
}
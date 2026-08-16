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
    /// <param name="inputs">The compilations to extract from, in the order they are declared.</param>
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
    ///     Project names to drop from the checked universe — the way a spec project that is itself a
    ///     member of the target solution stays out of its own check. Null excludes nothing.
    /// </param>
    /// <param name="targetFrameworks">
    ///     The per-project target frameworks the load reported (see
    ///     <see cref="SolutionExtensions.NormalizeProjectNames" />). Null — or a project absent from it —
    ///     leaves the framework unstamped, which is the single-framework norm.
    /// </param>
    /// <param name="declaredMembers">
    ///     The solution's declared <c>.csproj</c> membership (<see cref="SpecExclusion.TryReadDeclaredMembers" />),
    ///     stamped onto each project as <see cref="ProjectNode.SolutionMember" />. Null leaves every project
    ///     unlabeled, which is what an unreadable solution file must degrade to.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving Roslyn types before
    ///     <c>MSBuildLocator</c> registration in non-test hosts.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CodebaseModel> ExtractFromSolutionAsync(
        Solution solution,
        IReadOnlyCollection<string>? excludeProjects = null,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<CompilationInput> inputs = await CollectInputsAsync(
            solution, p => excludeProjects is null || !excludeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct);
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
        Solution solution,
        IReadOnlyCollection<string>? includeProjects,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<CompilationInput> inputs = await CollectInputsAsync(
            solution, p => includeProjects is null || includeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct);
        return inputs.Select(FragmentExtractor.Extract).ToList();
    }

    // The shared project enumeration behind both entry points: C# projects passing the filter, ordered by
    // (name, target framework), each turned into a CompilationInput carrying its forward project-reference
    // names and its framework. One enumeration means the extract-all-then-merge cold path, the
    // extract-fragments cache path, and cache hits all order identically.
    //
    // The framework tiebreak is load-bearing, not cosmetic. One project file's several compilations now share
    // a name, so ordering by name alone would leave them in whatever order the solution enumerated them —
    // <TargetFrameworks> declaration order — and the merge gives the FIRST input's facts to every type they
    // share. Ordering on the framework fixes which one that is, so reordering a csproj's framework list
    // cannot silently move a type's facts.
    private static async Task<List<CompilationInput>> CollectInputsAsync(
        Solution solution,
        Func<Project, bool> include,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks,
        IReadOnlySet<string>? declaredMembers,
        CancellationToken ct)
    {
        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(include)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => TargetFrameworkOf(targetFrameworks, p) ?? "", StringComparer.Ordinal)
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

            inputs.Add(new CompilationInput(
                compilation, project.Name, projectReferences, TargetFrameworkOf(targetFrameworks, project),
                SpecExclusion.SolutionMembershipOf(declaredMembers, project.FilePath)));
        }

        return inputs;
    }

    private static string? TargetFrameworkOf(IReadOnlyDictionary<ProjectId, string>? targetFrameworks, Project project)
    {
        if (targetFrameworks is null) return null;

        return targetFrameworks.TryGetValue(project.Id, out string? targetFramework) ? targetFramework : null;
    }
}

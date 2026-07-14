using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Codebase;

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
        var projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(p => excludeProjects is null || !excludeProjects.Contains(p.Name))
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

        return CodebaseModelBuilder.Build(inputs);
    }
}
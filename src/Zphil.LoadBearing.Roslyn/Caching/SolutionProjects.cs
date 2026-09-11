using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Reduces a loaded <see cref="Solution" /> to the per-project identity both structure-keyed caches
///     fingerprint: one <see cref="ProjectInputs" /> per C# project, keyed by name, in ordinal name order.
/// </summary>
/// <remarks>
///     <para>
///         One walk serves both stores. The extraction cache and the build capture each collapse a
///         multi-target-framework project's several <see cref="Project" />s onto the one name
///         <see cref="SolutionExtensions.NormalizeProjectNames">the load boundary normalized them to</see>,
///         and the two answers must agree — they decide, from the same solution, what "the structure moved"
///         means, and a project one of them collapses differently is a cached model served against inputs
///         the other never watched. Collapsed here, the agreement is by construction.
///     </para>
///     <para>
///         <b>First framework wins for identity, union across frameworks for documents.</b> The project file,
///         its directory, and the evaluated output pair come from whichever framework was seen first: the
///         intermediate root is derived from the prefix that pair shares, and that root is the same
///         whichever framework's pair it starts from. Documents union into an ordinal-sorted set, so a change
///         under any one framework still dirties the single entry, and the ordering is stable for a
///         fingerprint that must not move when the workspace enumerates differently.
///     </para>
///     <para>
///         <b>The one deliberate difference is <c>includeDocument</c>.</b> The two stores
///         disagree about build-output-generated sources — the fragment cache excludes them because their
///         mtimes churn on every build, the capture records them because they are csc inputs replay cannot
///         regenerate — and that disagreement is the whole of it. Passing it in keeps the difference
///         nameable at each call site instead of buried in a second copy of the walk.
///     </para>
/// </remarks>
internal static class SolutionProjects
{
    /// <summary>Collects one <see cref="ProjectInputs" /> per C# project, in ordinal name order.</summary>
    /// <param name="solution">The loaded (or replayed) solution to reduce.</param>
    /// <param name="includeDocument">
    ///     Whether to track a document, given the directory of the project it was found under and the
    ///     document's absolute path. Asked per <see cref="Project" />, so a multi-target-framework project
    ///     answers it against the framework the document came from.
    /// </param>
    internal static IReadOnlyList<ProjectInputs> Collect(
        Solution solution, Func<string, string, bool> includeDocument)
    {
        var byName = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp || project.FilePath is null) continue;

            string csprojPath = Path.GetFullPath(project.FilePath);
            string projectDirectory = Path.GetDirectoryName(csprojPath)!;

            if (!byName.TryGetValue(project.Name, out Accumulator? accumulator))
            {
                accumulator = new Accumulator(
                    project.Name,
                    csprojPath,
                    projectDirectory,
                    project.OutputFilePath,
                    project.CompilationOutputInfo.AssemblyPath);
                byName[project.Name] = accumulator;
            }

            foreach (Document document in project.Documents)
            {
                if (document.FilePath is null) continue;

                string documentPath = Path.GetFullPath(document.FilePath);
                if (includeDocument(projectDirectory, documentPath)) accumulator.Documents.Add(documentPath);
            }

            foreach (ProjectReference reference in project.ProjectReferences)
                if (solution.GetProject(reference.ProjectId)?.Name is { } referenceName)
                    accumulator.References.Add(referenceName);
        }

        return byName.Values
            .OrderBy(accumulator => accumulator.ProjectName, StringComparer.Ordinal)
            .Select(accumulator => accumulator.ToInputs())
            .ToList();
    }

    // Accumulates one project's identity and its unioned, ordinal-sorted document and reference sets across
    // frameworks.
    private sealed class Accumulator(
        string projectName,
        string csprojPath,
        string projectDirectory,
        string? evaluatedOutputPath,
        string? intermediateAssemblyPath)
    {
        public string ProjectName { get; } = projectName;
        public SortedSet<string> Documents { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> References { get; } = new(StringComparer.Ordinal);

        public ProjectInputs ToInputs()
        {
            return new ProjectInputs(
                ProjectName,
                csprojPath,
                projectDirectory,
                References.ToList(),
                Documents.ToList(),
                evaluatedOutputPath,
                intermediateAssemblyPath);
        }
    }
}

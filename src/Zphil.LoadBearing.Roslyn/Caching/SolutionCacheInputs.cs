using Microsoft.CodeAnalysis;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     Reduces a loaded <see cref="Solution" /> to the <see cref="ProjectInputs" /> the extraction cache
///     fingerprints (Phase 11 D2): one entry per C# project, keyed by name, with its project file, its
///     directory (whose cone the store scans for added <c>*.cs</c>), its forward project-reference names
///     (the Merkle dependency edges), and its source-document paths.
/// </summary>
/// <remarks>
///     <para>
///         A multi-target-framework project surfaces as several <see cref="Project" />s that share one name
///         and one project file; they collapse to a single <see cref="ProjectInputs" /> whose documents and
///         references are the union across frameworks, so a change under any framework still dirties the one
///         project entry. Document paths are absolute and ordinal-sorted for a stable fingerprint; the store
///         re-sorts internally, so this ordering is for determinism, not correctness.
///     </para>
///     <para>
///         <b>Generated documents under <c>bin</c>/<c>obj</c> are deliberately not tracked.</b> A project's
///         document set includes MSBuild-generated sources (<c>*.AssemblyInfo.cs</c>, <c>*.GlobalUsings.g.cs</c>,
///         analyzer/source-generator output), whose mtime — and sometimes bytes — churn on every design-time
///         build even when nothing the model depends on has changed. Their content is a pure function of the
///         structural inputs already fingerprinted (the project file, its assets, and the on-disk source), and
///         they declare no types themselves, so fingerprinting them would only manufacture false dirties on any
///         actively-built solution (LoadBearing's own repo included). Excluding them also aligns with the
///         store's cone scan, which already skips <c>bin</c>/<c>obj</c>.
///     </para>
/// </remarks>
internal static class SolutionCacheInputs
{
    /// <summary>Collects one <see cref="ProjectInputs" /> per C# project, in ordinal name order.</summary>
    internal static IReadOnlyList<ProjectInputs> Collect(Solution solution)
    {
        var byName = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (Project project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp || project.FilePath is null) continue;

            if (!byName.TryGetValue(project.Name, out Accumulator? accumulator))
            {
                accumulator = new Accumulator(project.Name, Path.GetFullPath(project.FilePath));
                byName[project.Name] = accumulator;
            }

            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(project.FilePath))!;
            foreach (Document document in project.Documents)
            {
                if (document.FilePath is null) continue;

                string full = Path.GetFullPath(document.FilePath);
                if (!BuildOutputDirectories.IsUnderBuildOutput(projectDirectory, full)) accumulator.Documents.Add(full);
            }

            foreach (ProjectReference reference in project.ProjectReferences)
                if (solution.GetProject(reference.ProjectId)?.Name is { } referenceName)
                    accumulator.References.Add(referenceName);
        }

        return byName.Values
            .OrderBy(a => a.ProjectName, StringComparer.Ordinal)
            .Select(a => a.ToInputs())
            .ToList();
    }

    private sealed class Accumulator(string projectName, string csprojPath)
    {
        public string ProjectName { get; } = projectName;
        public SortedSet<string> Documents { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> References { get; } = new(StringComparer.Ordinal);

        public ProjectInputs ToInputs()
        {
            return new ProjectInputs(
                ProjectName,
                csprojPath,
                Path.GetDirectoryName(csprojPath)!,
                References.ToList(),
                Documents.ToList());
        }
    }
}
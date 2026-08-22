using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn.Solutions;

internal static class SolutionExtensions
{
    /// <summary>
    ///     Removes <see cref="UnresolvedAnalyzerReference" /> and
    ///     <see cref="UnresolvedMetadataReference" /> instances from all projects, returning the
    ///     cleaned solution and counts of each removed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unresolved analyzer references crash Roslyn cross-project traversal APIs (SymbolFinder,
    ///         Renamer) with a switch-expression failure; unresolved metadata references are stripped
    ///         defensively for the same reason. This is a read-only transform — the returned
    ///         <see cref="Solution" /> is carried forward, never applied back to the workspace, so csproj
    ///         files on disk are left untouched.
    ///     </para>
    ///     <para>
    ///         Each project's surviving references are set in one call rather than removed one at a time,
    ///         because every removal forks the whole <see cref="Solution" /> and the case this transform
    ///         exists for is the unrestored solution, where <em>every</em> reference is unresolved: one fork
    ///         per project instead of one per reference. A project with nothing unresolved is not forked at
    ///         all.
    ///     </para>
    /// </remarks>
    public static (Solution Solution, int AnalyzerCount, int MetadataCount) StripUnresolvedReferences(this Solution solution)
    {
        var analyzerCount = 0;
        var metadataCount = 0;

        foreach (Project project in solution.Projects.ToList())
        {
            List<AnalyzerReference> resolvedAnalyzers = project.AnalyzerReferences
                .Where(analyzerRef => analyzerRef is not UnresolvedAnalyzerReference)
                .ToList();
            int unresolvedAnalyzers = project.AnalyzerReferences.Count - resolvedAnalyzers.Count;
            if (unresolvedAnalyzers > 0)
            {
                solution = solution.WithProjectAnalyzerReferences(project.Id, resolvedAnalyzers);
                analyzerCount += unresolvedAnalyzers;
            }

            List<MetadataReference> resolvedMetadata = project.MetadataReferences
                .Where(metadataRef => metadataRef is not UnresolvedMetadataReference)
                .ToList();
            int unresolvedMetadata = project.MetadataReferences.Count - resolvedMetadata.Count;
            if (unresolvedMetadata > 0)
            {
                solution = solution.WithProjectMetadataReferences(project.Id, resolvedMetadata);
                metadataCount += unresolvedMetadata;
            }
        }

        return (solution, analyzerCount, metadataCount);
    }

    /// <summary>
    ///     Renames every project back to the name its <c>.csproj</c> carries, returning the renamed solution
    ///     and the target framework each renamed project was discriminated by.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why one spelling.</b> Roslyn's <c>MSBuildProjectLoader</c> appends a <c>(tfm)</c>
    ///         discriminator whenever one project file yields more than one <see cref="Project" />, and
    ///         everything downstream keys on the undecorated csproj name. Normalizing here — at the load
    ///         boundary, where all three producers of a <see cref="Solution" /> meet — makes
    ///         <see cref="Project.Name" /> simply <em>be</em> the right string, so no reader can get it
    ///         wrong.
    ///     </para>
    ///     <para>
    ///         The clean name is read from the project file rather than parsed out of the decorated one: a
    ///         csproj genuinely named <c>Foo(x).csproj</c> would be mangled by stripping a trailing
    ///         <c>(...)</c>, while the file path is exact. The grouping is the precise inverse of Roslyn's
    ///         own guard — a group of one is left untouched, discriminator or not.
    ///     </para>
    ///     <para>
    ///         Two <em>different</em> csprojs that share a file name still collapse to one
    ///         <see cref="Project.Name" />. That is pre-existing (Roslyn only disambiguates within one
    ///         project file) and is exactly what the merge's per-type conflation note discloses — it is not
    ///         something this normalization introduces.
    ///     </para>
    ///     <para>
    ///         The returned map is keyed by <see cref="ProjectId" />, which survives every later
    ///         <see cref="Solution.WithDocumentText(DocumentId,Microsoft.CodeAnalysis.Text.SourceText,PreservationMode)" />,
    ///         so a caller may carry it alongside an edited snapshot for the whole load generation. A
    ///         single-framework solution pays nothing: no name differs, so no fork is taken and the map is
    ///         empty.
    ///     </para>
    /// </remarks>
    public static (Solution Solution, IReadOnlyDictionary<ProjectId, string> TargetFrameworks)
        NormalizeProjectNames(this Solution solution)
    {
        var canonicalProjectFiles = new ProjectFileCanonicalizer();
        var byProjectFile = new Dictionary<string, List<Project>>(StringComparer.Ordinal);
        foreach (Project project in solution.Projects)
        {
            // A project with no file path cannot be grouped with anything: folding the unknown-path projects
            // together would decorate genuinely unrelated projects, and '\0' cannot occur in a path so the
            // per-project sentinel cannot collide with a real key.
            string key = canonicalProjectFiles.Resolve(project.FilePath) is { } canonicalFile
                ? PathComparison.Fold(canonicalFile)
                : $"\0{project.Id.Id}";

            if (!byProjectFile.TryGetValue(key, out List<Project>? group)) byProjectFile[key] = group = [];
            group.Add(project);
        }

        var targetFrameworks = new Dictionary<ProjectId, string>();
        foreach (List<Project> group in byProjectFile.Values)
        {
            // The exact inverse of Roslyn's addDiscriminator guard: one Project per project file means the
            // name was never decorated, so there is nothing to undo and no framework to report.
            if (group.Count == 1) continue;

            foreach (Project project in group)
            {
                string cleanName = Path.GetFileNameWithoutExtension(project.FilePath!);
                string? targetFramework = DiscriminatorIn(project.Name, cleanName)
                                          ?? OutputDirectoryLeaf(project.OutputFilePath);
                if (targetFramework is not null) targetFrameworks[project.Id] = targetFramework;

                // WithProjectName forks the solution, so rename only what actually differs.
                if (!string.Equals(project.Name, cleanName, StringComparison.Ordinal))
                    solution = solution.WithProjectName(project.Id, cleanName);
            }
        }

        return (solution, targetFrameworks);
    }

    // The MSBuild arm. MSBuildProjectLoader spells a discriminated project exactly
    // "<csproj file name>(<TargetFramework>)", so the framework is the text between that prefix and the
    // trailing ')'. An undecorated (or otherwise-shaped) name yields null and the caller falls through.
    private static string? DiscriminatorIn(string projectName, string cleanName)
    {
        if (projectName.Length <= cleanName.Length + 2) return null;
        if (!projectName.StartsWith(cleanName, StringComparison.Ordinal)) return null;
        if (projectName[cleanName.Length] != '(' || projectName[^1] != ')') return null;

        return projectName.Substring(cleanName.Length + 1, projectName.Length - cleanName.Length - 2);
    }

    // The replay arm. Basic.CompilerLog.Util's SolutionReader applies no discriminator at all, so the name
    // carries nothing — but the build recorded an output path of the form <obj>/Debug/net10.0/Foo.dll, whose
    // leaf directory is the framework. The same read SpecResolver's sibling-configuration probe performs.
    // A project that suppresses AppendTargetFrameworkToOutputPath has no framework to report, and the
    // framework-naming merge note degrades to silence rather than to a wrong name.
    private static string? OutputDirectoryLeaf(string? outputFilePath)
    {
        if (string.IsNullOrEmpty(outputFilePath)) return null;

        string? directory = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrEmpty(directory)) return null;

        string leaf = Path.GetFileName(directory);
        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }
}

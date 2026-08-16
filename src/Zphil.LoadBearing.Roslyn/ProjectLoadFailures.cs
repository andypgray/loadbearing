using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Which projects failed to load, answered from the <em>structure</em> of the loaded
///     <see cref="Solution" /> rather than from the text of any MSBuild message — the fact the fail-closed
///     gate keys on.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why not the diagnostics.</b> Roslyn's <c>DiagnosticReporter</c> re-wraps every project-load log
///         item as <see cref="WorkspaceDiagnosticKind.Failure" /> with the item's real kind discarded, so an
///         MSBuild <em>warning</em> and a fatal evaluation error arrive at a host looking identical, and it
///         records <c>BuildEventArgs.Message</c> and never <c>.Code</c>. A gate that reads that stream can
///         only guess from wording, and wording is neither a contract nor language-independent: a NuGet
///         "will not be pruned" advisory refused a solution whose rules all passed, and the same audit-fetch
///         failure that exits 0 in English exited 2 in German. The one <c>ILogger</c> overload that looks
///         like an escape hatch is not one — <c>MSBuildProjectLoader</c> passes it only to
///         <c>IsBinaryLogger</c>, so capturing real codes would cost a binlog write and parse per run.
///     </para>
///     <para>
///         <b>What is available instead.</b> Roslyn already computes the answer and then throws the label
///         away: a project whose evaluation produced a real failure becomes
///         <c>ProjectFileInfo.CreateEmpty</c>, and a solution member whose <c>.csproj</c> is not on disk
///         never becomes a <see cref="Project" /> at all. Both leave a shape in the loaded solution, and both
///         yield a project <em>path</em> — which is why a refusal can name what failed rather than quote a
///         sentence about it.
///     </para>
///     <para>
///         <b>The two arms, and the measurements that chose them.</b> Loaded shapes were recorded across ten
///         beds before this predicate was written, because a code read had already been wrong about this area
///         once.
///         <list type="number">
///             <item>
///                 <b>Declared but absent</b> — a <c>.csproj</c> the solution file declares that produced no
///                 <see cref="Project" />.
///             </item>
///             <item>
///                 <b>Loaded but empty</b> — a <see cref="Project" /> with neither an evaluated
///                 <see cref="Project.OutputFilePath" /> nor a
///                 <see cref="CompilationOutputInfo.AssemblyPath" />. That is the <c>CreateEmpty</c> shape:
///                 measured on a project naming an unresolvable SDK and on one whose csproj XML is malformed,
///                 both of which loaded with zero documents, zero analyzer references, one metadata reference
///                 (<c>mscorlib</c>) and both paths null, while all twelve healthy projects measured carried
///                 both paths.
///             </item>
///         </list>
///         The counts are deliberately <em>not</em> the predicate. Zero metadata references never occurs —
///         a failed project keeps <c>mscorlib</c> — and zero analyzer references occurs on healthy projects
///         (a non-SDK-style .NET Framework project, and the <c>netstandard2.0</c> leg of a
///         multi-target-framework one), so either would refuse a solution that loaded perfectly well. Both
///         arms are also unaffected by <see cref="SolutionExtensions.StripUnresolvedReferences" />, which
///         removed nothing in any bed measured.
///     </para>
///     <para>
///         <b>Known limit: an unrestored solution is invisible here.</b> A project with no
///         <c>project.assets.json</c> still completes the design-time build and loads with its full document
///         and reference set, both output paths included — and raises no workspace diagnostic either, so it
///         gated under the message-matching predicate no more than it does under this one. Nothing detects
///         it today; <c>check</c> answers against whatever the load produced.
///     </para>
///     <para>
///         <b>Known limit: arm 1 skips a solution filter.</b> A <c>.slnf</c> legitimately loads a subset —
///         measured: a filter naming one of two members loads exactly that one — so treating its dropped
///         members as failures would refuse every filtered solution. The arm is therefore guarded by
///         <see cref="SolutionProjectFileParser.OwnsFormat" /> and simply does not run for a filter. That a
///         narrowed universe is announced nowhere is a separate, tracked gap, not this type's to close.
///     </para>
/// </remarks>
internal static class ProjectLoadFailures
{
    /// <summary>
    ///     The absolute <c>.csproj</c> paths of the projects that failed to load, ordinal-sorted and
    ///     deduplicated by this OS's path rule — empty for a solution that loaded completely.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="solutionPath">
    ///     The solution file the load came from, or null when there is none to read declared membership from
    ///     (a binlog replay, which reconstructs a solution from compiler invocations). Arm 1 needs a solution
    ///     file and is skipped without one; arm 2 runs either way.
    /// </param>
    internal static IReadOnlyList<string> Detect(Solution solution, string? solutionPath)
    {
        var failed = new HashSet<string>(PathComparison.Comparer);

        // Arm 2 first, over what did load: a multi-target-framework project is several Projects behind one
        // file path, so the set collapses them to the one csproj a reader would go and fix.
        foreach (Project project in solution.Projects)
            if (LoadedEmpty(project) && project.FilePath is { } filePath)
                failed.Add(Path.GetFullPath(filePath));

        if (solutionPath is not null && SolutionProjectFileParser.OwnsFormat(solutionPath))
        {
            var loadedFiles = solution.Projects
                .Select(project => project.FilePath)
                .Where(filePath => !string.IsNullOrEmpty(filePath))
                .Select(filePath => Path.GetFullPath(filePath!))
                .ToHashSet(PathComparison.Comparer);

            foreach (string declared in SolutionProjectFileParser.ReadCsprojMembers(solutionPath))
                if (!loadedFiles.Contains(declared))
                    failed.Add(declared);
        }

        return failed
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    // The CreateEmpty shape. Both paths are asked for rather than either, so a false positive — which is a
    // refusal of a healthy solution, the very defect this predicate replaces — would need two independent
    // Roslyn facts to go missing at once. They moved together in every bed measured.
    private static bool LoadedEmpty(Project project)
    {
        return project.OutputFilePath is null && project.CompilationOutputInfo.AssemblyPath is null;
    }
}

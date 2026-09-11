using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Roslyn.Solutions;

namespace Zphil.LoadBearing.Roslyn.Diagnostics;

/// <summary>
///     What one load produced, in the three shapes a verdict has to distinguish: the projects that
///     <see cref="Failed" /> — which gates — the declared members left <see cref="Unchecked" /> by a
///     solution filter, which narrows the verdict without invalidating it, and the
///     <see cref="Unsupported" /> projects no load could have produced, which state the run's coverage.
/// </summary>
/// <param name="Failed">
///     Absolute <c>.csproj</c> paths of the projects that failed to load. A non-empty list makes the model
///     wrong rather than smaller, so it is what <c>WorkspaceDiagnostics.IsIncomplete</c> keys on.
/// </param>
/// <param name="Unchecked">
///     Absolute <c>.csproj</c> paths the solution declares that this run neither loaded nor blamed — empty
///     unless the run went through a solution filter. A narrowed universe is a smaller true answer, not a
///     broken one, so this must never gate; it exists so a green cannot be mistaken for a green over the
///     whole solution.
/// </param>
/// <param name="Unsupported">
///     The projects the solution declares that no extractor reaches, each with its
///     <see cref="UnsupportedProjectKind" />, read straight off the solution file rather than measured
///     against the load. Nothing here was ever going to load, so — like <see cref="Unchecked" /> and unlike
///     <see cref="Failed" /> — it must never gate: the model is smaller than the solution, not wrong about it.
/// </param>
internal sealed record ProjectLoadReport(
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Unchecked,
    IReadOnlyList<UnsupportedProject> Unsupported)
{
    /// <summary>The report for a load with nothing to say — no failures, no narrowing, nothing unreadable.</summary>
    internal static ProjectLoadReport Empty { get; } = new([], [], []);
}

/// <summary>
///     Which projects failed to load, answered from the <em>structure</em> of the loaded
///     <see cref="Solution" /> rather than from the text of any MSBuild message — the fact the fail-closed
///     gate keys on.
/// </summary>
/// <remarks>
///     <para>
///         Both arms are structural, and no message text is an input to any verdict. Arm 1: a
///         <c>.csproj</c> the solution file declares that produced no <see cref="Project" />. Arm 2: a
///         <see cref="Project" /> carrying neither an evaluated <see cref="Project.OutputFilePath" /> nor a
///         <see cref="CompilationOutputInfo.AssemblyPath" /> — the shape Roslyn leaves behind when a
///         project's evaluation produced a real failure.
///     </para>
///     <para>
///         The restore half is <see cref="RestoreFailures" />', not this predicate's: a project whose NuGet
///         packages did not resolve loads completely and presents neither arm, so a sibling reads it off
///         disk where this one reads only the loaded <see cref="Solution" />. Both feed one gate; the lists
///         stay separate because these projects loaded and the remedy differs.
///     </para>
///     <para>
///         A solution filter narrows arm 1 rather than disabling it: the arm reads
///         <see cref="SolutionMembership.Required" />, so a selected project that failed to load is blamed
///         like any other. <see cref="ProjectLoadReport.Unchecked" /> is the subtraction against
///         <see cref="SolutionMembership.Declared" />, deliberately <em>not</em> derived from filter text —
///         Roslyn loads a filter's projects plus their transitive <c>ProjectReference</c> closure, so filter
///         text overstates the gap. <see cref="ProjectLoadReport.Unsupported" /> rides along unmeasured,
///         read off the solution file and outside both subtractions: a project no extractor reaches was
///         never owed, so a polyglot solution cannot be refused for holding one.
///     </para>
/// </remarks>
internal static class ProjectLoadFailures
{
    /// <summary>
    ///     What the load did and did not produce: the projects that failed, and the declared members a
    ///     solution filter left unchecked. Both are absolute <c>.csproj</c> paths, ordinal-sorted and
    ///     deduplicated by this OS's path rule; both are empty for a solution that loaded completely.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="solutionPath">
    ///     The solution file the load came from, or null when there is none to read declared membership from
    ///     (a binlog replay, which reconstructs a solution from compiler invocations). Arm 1 needs a solution
    ///     file and is skipped without one; arm 2 runs either way.
    /// </param>
    internal static ProjectLoadReport Detect(Solution solution, string? solutionPath)
    {
        var failed = new HashSet<string>(PathComparison.Comparer);

        // Arm 2 first, over what did load: a multi-target-framework project is several Projects behind one
        // file path, so the set collapses them to the one csproj a reader would go and fix. A project in
        // another language is skipped — see NotACsharpProject on why it must not be able to gate.
        foreach (Project project in solution.Projects)
            if (!NotACsharpProject(project) && LoadedEmpty(project) && project.FilePath is { } filePath)
                failed.Add(Path.GetFullPath(filePath));

        var uncheckedMembers = new HashSet<string>(PathComparison.Comparer);
        IReadOnlyList<UnsupportedProject> unsupported = [];

        if (solutionPath is not null && SolutionProjectFileParser.OwnsFormat(solutionPath))
        {
            HashSet<string> loadedFiles = solution.Projects
                .Select(project => project.FilePath)
                .Where(filePath => !string.IsNullOrEmpty(filePath))
                .Select(filePath => Path.GetFullPath(filePath!))
                .ToHashSet(PathComparison.Comparer);

            SolutionMembership membership = SolutionProjectFileParser.ReadDeclaredMembership(solutionPath);

            foreach (string required in membership.Required)
                if (!loadedFiles.Contains(required))
                    failed.Add(required);

            // Declared, not loaded, and not blamed — the only thing left for it to be is out of scope.
            foreach (string declared in membership.Declared)
                if (!loadedFiles.Contains(declared) && !failed.Contains(declared))
                    uncheckedMembers.Add(declared);

            // Not measured against the load at all: this is what the solution file says, and an entry here
            // was never a candidate to load. Sorted on the path, so every list on this report is ordered the
            // same way and a document reads the same on any OS — the kind rides along and orders nothing,
            // because a reader looks these up by project.
            unsupported = membership.Unsupported
                .OrderBy(project => project.Path, StringComparer.Ordinal)
                .ToList();
        }

        return new ProjectLoadReport(Sorted(failed), Sorted(uncheckedMembers), unsupported);
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> paths)
    {
        return paths
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

    // F# reaches a loaded solution with its output paths — measured — so this arm can see a project the
    // model was never going to contain, and blaming one would refuse a healthy polyglot solution as a load
    // failure; the run states such a project under its coverage statement instead. A keep-it-that-way
    // guard rather than a fix for an observed refusal — unlike RestoreFailures', which was measured
    // falsely refusing.
    private static bool NotACsharpProject(Project project)
    {
        return !ProjectLanguages.IsCSharp(project);
    }
}

using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     What one load produced, in the two shapes a verdict has to distinguish: the projects that
///     <see cref="Failed" /> — which gates — and the declared members left <see cref="Unchecked" /> by a
///     solution filter, which narrows the verdict without invalidating it.
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
internal sealed record ProjectLoadReport(
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Unchecked)
{
    /// <summary>The report for a load with nothing to say — no failures, no narrowing.</summary>
    internal static ProjectLoadReport Empty { get; } = new([], []);
}

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
///         <b>The restore half is <see cref="RestoreFailures" />', not this predicate's.</b> A project whose
///         NuGet packages did not resolve still completes the design-time build and loads with its full
///         document and reference set, both output paths included, so it presents neither arm above — while
///         every edge its package references would have produced is missing from the model, which was
///         measured to turn a failing rule green. That is read off the project's own
///         <c>project.assets.json</c> — which a failed restore does write, and which a restore that never ran
///         leaves absent — by a sibling that reads disk where this one reads only the loaded
///         <see cref="Solution" />. Both feed one gate; the lists stay separate because these projects loaded
///         and the remedy differs.
///     </para>
///     <para>
///         <b>A <em>never</em>-restored solution is invisible to this predicate, and no longer to its sibling.</b>
///         A project with no <c>project.assets.json</c> at all loads exactly as a restored one
///         does and raises no workspace diagnostic, so it gated under the message-matching predicate no more
///         than it does under this one. What changed is what its absent assets file means to
///         <see cref="RestoreFailures" />: absence asserts nothing for a non-SDK-style .NET Framework project,
///         which never writes one and which this product explicitly supports, and asserts "the restore never
///         ran" for an SDK-style one, which writes one on every restore. Reading
///         <see cref="SdkStyleProject.IsSdkStyle" /> tells the two apart, so the sibling blames the second and
///         still leaves the first alone. What stays invisible is narrower: a non-SDK-style project using
///         <c>PackageReference</c> that was never restored, whose absent assets file cannot be told from a
///         <c>packages.config</c> project's.
///     </para>
///     <para>
///         <b>A solution filter narrows arm 1 rather than disabling it.</b> A <c>.slnf</c> legitimately
///         loads a subset, so its unselected members are not failures — but the arm used to be skipped
///         wholesale for a filter, which meant a selected project that genuinely failed to load was invisible
///         to the gate. Reading <see cref="SolutionMembership.Required" /> instead of the raw member list
///         keeps the false positives out <em>and</em> restores the arm: what a filter asked for and did not
///         get is a failure like any other. That is sound only because Roslyn refuses a filter naming a
///         non-member outright — measured, both for a <c>.csproj</c> absent from disk and for one present but
///         outside the solution — so every project a well-formed filter selects is one the load was obliged
///         to produce.
///     </para>
///     <para>
///         <b>The same subtraction names the narrowing.</b> Against
///         <see cref="SolutionMembership.Declared" /> — every member, filter or no filter — what neither
///         loaded nor failed is what the run simply did not check. That set is
///         <see cref="ProjectLoadReport.Unchecked" />, and it is deliberately <em>not</em> derived from the
///         filter text: Roslyn loads a filter's projects plus their transitive <c>ProjectReference</c>
///         closure, so a filter naming two of three projects routinely checks all three. Naming the third as
///         skipped would be a false claim of a gap, which is worse than announcing no narrowing at all. The
///         set is empty for every unfiltered solution, where <see cref="SolutionMembership.Required" /> and
///         <see cref="SolutionMembership.Declared" /> are the same list and anything missing has already been
///         blamed.
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
        // file path, so the set collapses them to the one csproj a reader would go and fix.
        foreach (Project project in solution.Projects)
            if (LoadedEmpty(project) && project.FilePath is { } filePath)
                failed.Add(Path.GetFullPath(filePath));

        var uncheckedMembers = new HashSet<string>(PathComparison.Comparer);

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
        }

        return new ProjectLoadReport(Sorted(failed), Sorted(uncheckedMembers));
    }

    private static IReadOnlyList<string> Sorted(HashSet<string> paths)
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
}

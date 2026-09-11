using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Roslyn.Solutions;

/// <summary>
///     One project as the exclusion walk sees it: the <see cref="Name" /> the checked universe filters on,
///     the <c>.csproj</c> <see cref="FilePath" /> declared membership is tested against, and the names of the
///     projects it references. Reduced to a tuple so the walk is unit-testable over synthetic graphs, with no
///     workspace and no disk.
/// </summary>
/// <remarks>
///     <see cref="CanonicalFilePath" /> is <see cref="FilePath" /> already canonicalized, for a producer that
///     resolved it anyway — spec resolution holds every project's canonical spelling by the time it asks this
///     walk anything. It is defaulted and trailing so the synthetic graphs stay two-and-a-list, which is the
///     tuple shape that keeps the pure core testable with no disk: a slot only a workspace-backed caller can
///     fill must not become one every caller has to spell.
/// </remarks>
internal sealed record SpecExclusionProject(
    string Name,
    string? FilePath,
    IReadOnlyList<string> ProjectReferenceNames,
    string? CanonicalFilePath = null);

/// <summary>
///     Which projects a solution-member spec drops from the checked universe:
///     <c>{spec project} ∪ (transitive ProjectReference closure of the spec project ∖ declared members)</c>.
///     A spec project's own <c>ProjectReference</c>s drag the contract library — and any rule-pack library it
///     composes from — into the workspace as passengers, and a passenger is not solution material just
///     because MSBuild loaded it. What the solution <em>declares</em> is.
/// </summary>
/// <remarks>
///     <para>
///         <b>The subtraction is load-bearing and must not collapse to "the closure".</b> A spec routinely
///         references the very code it governs: an example's spec references its application project, and
///         this repo's own spec references the two projects it dogfoods. Excluding a raw closure would drop
///         the codebase under law and quietly report green over nothing. Declared membership is the only
///         sound discriminator — it says which projects the solution claims, independently of who happens to
///         reference whom.
///     </para>
///     <para>
///         <b>Never fail open into an empty universe.</b> When membership cannot be read — an unreadable or
///         malformed solution file, a malformed filter, or a format
///         <see cref="SolutionProjectFileParser.OwnsFormat">the parser does not own</see> — the answer
///         degrades to <c>{spec project}</c> alone. An unparsed solution reads as zero declared members, so
///         the alternative would subtract the whole closure. For the same reason a project whose path is
///         unknown counts as declared: keeping a project in the universe is the safe direction, because
///         wrongly excluding one shrinks the codebase under law silently.
///     </para>
///     <para>
///         <b>A solution filter reads through to the solution it filters</b>, and against that solution's
///         <see cref="SolutionMembership.Declared">whole</see> membership rather than the filter's
///         selection: membership answers "is this project solution material?", which a filter does not
///         change. Selecting instead would exclude a member a reference dragged in anyway — the silent
///         shrink this type exists to prevent.
///     </para>
/// </remarks>
internal static class SpecExclusion
{
    /// <summary>
    ///     The exclusion set for <paramref name="specProjectName" /> over a loaded
    ///     <paramref name="solution" />, against an already-read <paramref name="declaredMembers" /> set
    ///     (<see langword="null" /> ⇒ membership unreadable ⇒ the <c>{spec project}</c> fallback). For a
    ///     caller with no canonical view of the solution's project files; one that has already resolved them
    ///     — spec resolution does — projects its own tuples and calls the pure core directly.
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(
        Solution solution, IReadOnlySet<string>? declaredMembers, string specProjectName)
    {
        var canonicalProjectFiles = new ProjectFileCanonicalizer();
        List<SpecExclusionProject> projects = solution.Projects
            .Select(project => new SpecExclusionProject(
                project.Name,
                project.FilePath,
                project.ProjectReferences
                    .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!)
                    .ToList(),
                canonicalProjectFiles.Resolve(project.FilePath)))
            .ToList();

        return Compute(projects, declaredMembers, specProjectName);
    }

    /// <summary>
    ///     The pure core, testable over a synthetic project graph: breadth-first from
    ///     <paramref name="specProjectName" /> across <see cref="SpecExclusionProject.ProjectReferenceNames" />,
    ///     excluding the spec project plus every project reached that <paramref name="declaredMembers" /> does
    ///     not declare. Ordinal-sorted so the set serializes to stable bytes in the extraction cache.
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(
        IReadOnlyList<SpecExclusionProject> projects,
        IReadOnlySet<string>? declaredMembers,
        string specProjectName)
    {
        if (declaredMembers is null) return [specProjectName];

        var referencesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var declaredByName = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (SpecExclusionProject project in projects)
        {
            if (!referencesByName.TryGetValue(project.Name, out List<string>? references))
                referencesByName[project.Name] = references = [];
            references.AddRange(project.ProjectReferenceNames);

            // A multi-targeted project arrives once per framework, under the one name the load boundary
            // normalized its Projects to; declared by any entry is declared, and its reference edges union.
            declaredByName[project.Name] = declaredByName.GetValueOrDefault(project.Name)
                                           || IsDeclaredMember(
                                               declaredMembers, project.FilePath, project.CanonicalFilePath);
        }

        var excluded = new SortedSet<string>(StringComparer.Ordinal) { specProjectName };
        var visited = new HashSet<string>(StringComparer.Ordinal) { specProjectName };
        var pending = new Queue<string>();
        pending.Enqueue(specProjectName);

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!referencesByName.TryGetValue(name, out List<string>? references)) continue;

            foreach (string reference in references)
            {
                if (!visited.Add(reference)) continue;
                pending.Enqueue(reference);

                // Absent from the graph ⇒ nothing to exclude: it is not in the universe to begin with.
                if (!declaredByName.GetValueOrDefault(reference, true)) excluded.Add(reference);
            }
        }

        return excluded;
    }

    /// <summary>
    ///     The solution's declared <c>.csproj</c> members, canonicalized for comparison, or
    ///     <see langword="null" /> when membership cannot be read — an unowned format or any read/parse
    ///     failure, a malformed solution filter included. Null is the fallback signal, never an empty set.
    /// </summary>
    internal static IReadOnlySet<string>? TryReadDeclaredMembers(string solutionPath)
    {
        if (!SolutionProjectFileParser.OwnsFormat(solutionPath)) return null;

        try
        {
            return CanonicalMemberSet(SolutionProjectFileParser.ReadDeclaredMembership(solutionPath).Declared);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Canonicalizes <paramref name="csprojPaths" /> into a per-OS comparable set (symlinks resolved,
    ///     <see cref="PathComparison" /> casing), the same standard both sides of every membership test use.
    /// </summary>
    internal static IReadOnlySet<string> CanonicalMemberSet(IEnumerable<string> csprojPaths)
    {
        return new HashSet<string>(csprojPaths.Select(PathCanonicalizer.Resolve), PathComparison.Comparer);
    }

    /// <summary>
    ///     Whether <paramref name="projectFilePath" /> is one of <paramref name="declaredMembers" />.
    ///     Unreadable membership and an unknown project path both answer <see langword="true" />: neither
    ///     disproves membership, and a false negative would silently shrink the checked universe. A caller
    ///     holding the canonical spelling already passes it as <paramref name="canonicalProjectFilePath" />
    ///     rather than have it derived a second time.
    /// </summary>
    /// <remarks>
    ///     The canonical slot is a caller-honoured guarantee, deliberately not a validated one: nothing can
    ///     afford to check it, because re-resolving in order to compare would pay exactly the
    ///     filesystem-probing walk the slot exists to avoid on every warm call. Pass what
    ///     <see cref="PathCanonicalizer.Resolve" /> would return for the same path, or pass nothing. A
    ///     spelling the solution file never declared answers "not a member" — the silent-shrink direction
    ///     this type says it must never be wrong in — which is why the slot is filled by the one caller
    ///     that resolved the path itself and left null by everyone else.
    /// </remarks>
    internal static bool IsDeclaredMember(
        IReadOnlySet<string>? declaredMembers, string? projectFilePath, string? canonicalProjectFilePath = null)
    {
        if (declaredMembers is null || string.IsNullOrEmpty(projectFilePath)) return true;

        return declaredMembers.Contains(canonicalProjectFilePath ?? PathCanonicalizer.Resolve(projectFilePath));
    }

    /// <summary>
    ///     The same question asked as a fact to <em>report</em> rather than a set to subtract:
    ///     <see langword="true" /> declared, <see langword="false" /> a passenger, <see langword="null" />
    ///     when nothing was read — unreadable membership, or a project whose path the load never reported.
    /// </summary>
    /// <remarks>
    ///     Deliberately not <see cref="IsDeclaredMember" /> with its answer widened. That method's
    ///     <see langword="true" /> for the unread cases is the safe direction for <em>exclusion</em>, where
    ///     the cost of guessing wrong is a silently shrunken universe; here the cost runs the other way, and
    ///     an answer of "declared" that no solution file was ever read for would be an asserted fact with
    ///     nothing behind it. The two callers want opposite fallbacks, so they get two methods.
    /// </remarks>
    internal static bool? SolutionMembershipOf(
        IReadOnlySet<string>? declaredMembers, string? projectFilePath, string? canonicalProjectFilePath = null)
    {
        if (declaredMembers is null || string.IsNullOrEmpty(projectFilePath)) return null;

        return IsDeclaredMember(declaredMembers, projectFilePath, canonicalProjectFilePath);
    }
}

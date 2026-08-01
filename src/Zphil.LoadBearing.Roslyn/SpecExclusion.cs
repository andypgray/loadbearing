using Microsoft.CodeAnalysis;
using Zphil.LoadBearing.Rendering;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     One project as the exclusion walk sees it: the <see cref="Name" /> the checked universe filters on,
///     the <c>.csproj</c> <see cref="FilePath" /> declared membership is tested against, and the names of the
///     projects it references. Reduced to a tuple so the walk is unit-testable over synthetic graphs, with no
///     workspace and no disk.
/// </summary>
internal sealed record SpecExclusionProject(string Name, string? FilePath, IReadOnlyList<string> ProjectReferenceNames);

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
///         malformed solution file, or a format
///         <see cref="SolutionProjectFileParser.OwnsFormat">the parser does not own</see> (a <c>.slnf</c>,
///         which <see cref="SolutionDiscovery" /> accepts) — the answer degrades to <c>{spec project}</c>,
///         the behaviour before this walk existed. An unparsed solution reads as zero declared members, so
///         the alternative would subtract the whole closure. For the same reason a project whose path is
///         unknown counts as declared: keeping a project in the universe is the safe direction, because
///         wrongly excluding one shrinks the codebase under law silently.
///     </para>
/// </remarks>
internal static class SpecExclusion
{
    /// <summary>
    ///     The exclusion set for <paramref name="specProjectName" />, reading the solution's declared
    ///     membership from <paramref name="solutionPath" />. Callers that already needed the membership
    ///     (spec resolution flags its candidates with it) pass their set to the overload instead of reading
    ///     the file twice.
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(Solution solution, string solutionPath, string specProjectName)
    {
        return Compute(solution, TryReadDeclaredMembers(solutionPath), specProjectName);
    }

    /// <summary>
    ///     The exclusion set for <paramref name="specProjectName" /> over a loaded
    ///     <paramref name="solution" />, against an already-read <paramref name="declaredMembers" /> set
    ///     (<see langword="null" /> ⇒ membership unreadable ⇒ the <c>{spec project}</c> fallback).
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(
        Solution solution, IReadOnlySet<string>? declaredMembers, string specProjectName)
    {
        var projects = solution.Projects
            .Select(project => new SpecExclusionProject(
                project.Name,
                project.FilePath,
                project.ProjectReferences
                    .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!)
                    .ToList()))
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
            if (!referencesByName.TryGetValue(project.Name, out var references))
                referencesByName[project.Name] = references = [];
            references.AddRange(project.ProjectReferenceNames);

            // A multi-targeted project arrives once per framework under one name; declared by any entry is
            // declared, and its reference edges union.
            declaredByName[project.Name] = declaredByName.GetValueOrDefault(project.Name)
                                           || IsDeclaredMember(declaredMembers, project.FilePath);
        }

        var excluded = new SortedSet<string>(StringComparer.Ordinal) { specProjectName };
        var visited = new HashSet<string>(StringComparer.Ordinal) { specProjectName };
        var pending = new Queue<string>();
        pending.Enqueue(specProjectName);

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!referencesByName.TryGetValue(name, out var references)) continue;

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
    ///     failure. Null is the fallback signal, never an empty set.
    /// </summary>
    internal static IReadOnlySet<string>? TryReadDeclaredMembers(string solutionPath)
    {
        if (!SolutionProjectFileParser.OwnsFormat(solutionPath)) return null;

        try
        {
            return CanonicalMemberSet(SolutionProjectFileParser.ReadCsprojMembers(solutionPath));
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
    ///     disproves membership, and a false negative would silently shrink the checked universe.
    /// </summary>
    internal static bool IsDeclaredMember(IReadOnlySet<string>? declaredMembers, string? projectFilePath)
    {
        if (declaredMembers is null || string.IsNullOrEmpty(projectFilePath)) return true;

        return declaredMembers.Contains(PathCanonicalizer.Resolve(projectFilePath));
    }
}
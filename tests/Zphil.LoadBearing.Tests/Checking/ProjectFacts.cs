using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Hand-built project fixtures for the packaging verbs (GRAMMAR §4.10): a
///     <see cref="CodebaseModel" /> carrying nothing but its <see cref="CodebaseModel.Projects" />.
/// </summary>
/// <remarks>
///     <para>
///         The MSBuild-free <c>CompilationFactory</c> path yields projects with no artifact facts at all,
///         which is exactly the absent-fact case and nothing else — so the rows that need a declared
///         framework, a package, or a tri-state flag build their projects here instead. A project verb
///         never reads a type, so an empty type universe costs these fixtures nothing.
///     </para>
///     <para>
///         <see cref="Project" /> names every optional fact, so a row states the facts it is about and
///         says nothing about the rest — and a reader of the row can tell "declared false" from "never
///         evaluated" at the call site rather than by counting nulls.
///     </para>
/// </remarks>
internal static class ProjectFacts
{
    /// <summary>A codebase whose only content is <paramref name="projects" />, in the order given.</summary>
    internal static CodebaseModel Solution(params ProjectNode[] projects)
    {
        return new CodebaseModel([], [], [], [], [], [], [], [], [], projects, [], []);
    }

    /// <summary>One project carrying whichever artifact facts the caller names; every other fact is unknown.</summary>
    internal static ProjectNode Project(
        string name,
        IReadOnlyList<string>? targetFrameworks = null,
        SourceLocation? targetFrameworksSite = null,
        IReadOnlyList<PackageReference>? packageReferences = null,
        bool? isPackable = null,
        SourceLocation? isPackableSite = null,
        bool? locksPackages = null,
        SourceLocation? locksPackagesSite = null)
    {
        return new ProjectNode(
            name,
            projectReferences: [],
            solutionMember: true,
            targetFrameworks: targetFrameworks,
            targetFrameworksSite: targetFrameworksSite,
            factsFollow: null,
            packageReferences: packageReferences,
            isPackable: isPackable,
            isPackableSite: isPackableSite,
            locksPackages: locksPackages,
            locksPackagesSite: locksPackagesSite);
    }

    /// <summary>One declared package reference at <paramref name="filePath" />:<paramref name="line" />.</summary>
    internal static PackageReference Package(string name, string filePath, int line)
    {
        return new PackageReference(name, Site(filePath, line));
    }

    /// <summary>A declaration site — the <c>file:line</c> a violation cites.</summary>
    internal static SourceLocation Site(string filePath, int line)
    {
        return new SourceLocation(filePath, line);
    }
}

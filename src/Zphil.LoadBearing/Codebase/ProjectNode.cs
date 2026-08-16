namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A project in the extracted model: its name, whether the solution file declares it, and its forward
///     project references (by name). The reverse graph is derived in memory by consumers when needed; only
///     the forward edges are stored, ordinal-ordered for determinism.
/// </summary>
public sealed class ProjectNode
{
    internal ProjectNode(string name, IReadOnlyList<string> projectReferences, bool? solutionMember = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        SolutionMember = solutionMember;
    }

    /// <summary>The project (assembly) name.</summary>
    public string Name { get; }

    /// <summary>The names of the projects this project references, ordinal-ordered.</summary>
    public IReadOnlyList<string> ProjectReferences { get; }

    /// <summary>
    ///     Whether the solution file declares this project, or <see langword="null" /> when membership was not
    ///     read. A workspace loads every project a <c>ProjectReference</c> reaches, which is a wider set than
    ///     the one the solution declares: <see langword="false" /> marks such a passenger, and it is a fact
    ///     about the solution rather than a defect in the project.
    /// </summary>
    /// <remarks>
    ///     <see langword="null" /> means unread, never "not a member" — an unreadable or unowned solution
    ///     format, a project whose file path the load never reported, or an extraction handed no membership
    ///     at all (every hand-built compilation). Consumers that filter on this must therefore treat unknown
    ///     as passing, or an unparsed solution would silently empty their view.
    /// </remarks>
    public bool? SolutionMember { get; }
}

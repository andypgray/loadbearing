namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A project in the extracted model: its name, whether the solution file declares it, its forward
///     project references (by name), and — where one project file yielded several compilations — the target
///     frameworks it was extracted from and the one whose facts its shared types carry. The reverse graph is
///     derived in memory by consumers when needed; only the forward edges are stored, ordinal-ordered for
///     determinism.
/// </summary>
public sealed class ProjectNode
{
    internal ProjectNode(
        string name,
        IReadOnlyList<string> projectReferences,
        bool? solutionMember = null,
        IReadOnlyList<string>? targetFrameworks = null,
        string? factsFollow = null)
    {
        Name = name;
        ProjectReferences = projectReferences;
        SolutionMember = solutionMember;
        TargetFrameworks = targetFrameworks ?? [];
        FactsFollow = factsFollow;
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

    /// <summary>
    ///     Every target framework this project's fragments carried, ordinal-ordered — which is also the order
    ///     a workspace load hands them to extraction in, so on that path the first entry is the one a shared
    ///     type's facts fall to. Empty for the common single-framework project and for every hand-built input,
    ///     because there is then no second compilation to distinguish a fact from: one project file, one view.
    /// </summary>
    /// <remarks>
    ///     A project with more than one entry is one <c>.csproj</c> that arrived as several Roslyn projects.
    ///     They union into this one node — its types, its references and its membership are the union — and the
    ///     union is lossless everywhere except <see cref="FactsFollow" />, which is the one thing it cannot be.
    /// </remarks>
    public IReadOnlyList<string> TargetFrameworks { get; }

    /// <summary>
    ///     The framework whose facts the types this project's frameworks <em>share</em> carry — the first
    ///     extracted — or <see langword="null" /> when nothing collapsed. A type each framework declares can
    ///     only carry one framework's edges, members and hierarchy, so a rule about it is checked against that
    ///     framework alone and whatever another framework's <c>#if</c> guards is not in the model at all.
    /// </summary>
    /// <remarks>
    ///     Null is deliberately not "the first of <see cref="TargetFrameworks" />": a project whose frameworks
    ///     share no type displaced nothing — every type keeps its own framework's facts — and naming a winner
    ///     there would be false about all of them. It is the same gate the merge's advisory note is under.
    /// </remarks>
    public string? FactsFollow { get; }
}

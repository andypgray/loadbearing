using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The project filter a <see cref="GraphDiagramRenderer" /> render is drawn through: an optional
///     allow-list and an optional deny-list of project-name globs. Empty <see cref="Only" /> means every
///     project; <see cref="Exclude" /> always wins. Patterns match the project (assembly) name as a single
///     token through <see cref="Wildcard" /> — <c>*</c> spans dots here, because a project name is one name
///     and not a dotted namespace path.
/// </summary>
/// <remarks>
///     The guardrail doc-001 records — diagrams stop reading above roughly twenty nodes — is a product
///     option rather than a caveat: a solution whose fixture or sample projects outnumber its shipping ones
///     names the six it wants drawn and the committed artifact stays stable while the rest churn.
/// </remarks>
public sealed class DiagramScope
{
    /// <summary>Creates a scope from an allow-list and a deny-list of project-name globs.</summary>
    public DiagramScope(IReadOnlyList<string> only, IReadOnlyList<string> exclude)
    {
        Only = Guard.NotNull(only, nameof(only));
        Exclude = Guard.NotNull(exclude, nameof(exclude));
    }

    /// <summary>Every project, filtered by nothing.</summary>
    public static DiagramScope Everything { get; } = new([], []);

    /// <summary>The allow-list globs; empty means every project is in scope.</summary>
    public IReadOnlyList<string> Only { get; }

    /// <summary>The deny-list globs; a match here drops the project whatever <see cref="Only" /> says.</summary>
    public IReadOnlyList<string> Exclude { get; }

    /// <summary>True when <paramref name="projectName" /> survives both lists.</summary>
    public bool Includes(string projectName)
    {
        Guard.NotNull(projectName, nameof(projectName));

        if (Exclude.Any(pattern => Wildcard.Match(pattern, projectName))) return false;

        return Only.Count == 0 || Only.Any(pattern => Wildcard.Match(pattern, projectName));
    }
}
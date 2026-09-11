using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The project filter a diagram is drawn through: an allow-list and a deny-list of project-name
///     globs. An empty <see cref="Only" /> means every project, and <see cref="Exclude" /> always wins. A
///     pattern is matched against the project (assembly) name as one whole token, case-sensitively, with
///     <c>*</c> standing for any run of characters, dots included, so <c>MyApp.*</c> reaches
///     <c>MyApp.Web.Api</c>. Filtering is how a large solution keeps a drawing legible, and how a
///     committed diagram stays put while sample or fixture projects come and go.
/// </summary>
public sealed class DiagramScope
{
    /// <summary>
    ///     Creates a filter from an allow-list and a deny-list of project-name globs. Neither list may be
    ///     null; pass two empty ones, or use <see cref="Everything" />, to filter nothing.
    /// </summary>
    public DiagramScope(IReadOnlyList<string> only, IReadOnlyList<string> exclude)
    {
        Only = Guard.NotNull(only, nameof(only));
        Exclude = Guard.NotNull(exclude, nameof(exclude));
    }

    /// <summary>
    ///     Gets every project, filtered by nothing.
    /// </summary>
    public static DiagramScope Everything { get; } = new([], []);

    /// <summary>
    ///     Gets the allow-list globs; empty means every project is in scope.
    /// </summary>
    public IReadOnlyList<string> Only { get; }

    /// <summary>
    ///     Gets the deny-list globs; a match here drops the project whatever <see cref="Only" /> says.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; }

    /// <summary>
    ///     Whether <paramref name="projectName" /> survives both lists: true when no <see cref="Exclude" />
    ///     pattern matches it and either <see cref="Only" /> is empty or one of its patterns does.
    /// </summary>
    public bool Includes(string projectName)
    {
        Guard.NotNull(projectName, nameof(projectName));

        if (Exclude.Any(pattern => Wildcard.Match(pattern, projectName))) return false;

        return Only.Count == 0 || Only.Any(pattern => Wildcard.Match(pattern, projectName));
    }
}

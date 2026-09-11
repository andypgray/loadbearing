using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A <em>family</em> — <c>arch.Each(dispatch, tracking, invoicing)</c> or
///     <c>arch.Each(arch.Projects.Matching("Nop.Plugin.*"))</c> — a noun that carries a partition of the
///     types it names into <em>cells</em> (GRAMMAR §5.1). Reference fragment: "each of the Dispatch,
///     Tracking and Invoicing layers", "each of the projects matching <c>`Nop.Plugin.*`</c>". A bare
///     family speaks in the collective voice (GRAMMAR §6).
/// </summary>
/// <remarks>
///     Exactly one of <see cref="Layers" /> and <see cref="Projects" /> is non-null, which is what the
///     two constructors guarantee. The cells are read by exactly three places — the <c>MustOnly*</c>
///     reference verbs' self-allowance, the three family verbs (the cross-cell ban, the inbound leaf and
///     the circular-references gate), and card placement — and to every other verb, walk and renderer a
///     family is the union of its cells.
///     <para>
///         A one-cell layer family renders exactly as the cell would, in both positions: the family verbs
///         and the per-cell self still apply to it, and a loop that built a family of one has said the same
///         thing the cell says (the §5.1 union identity). The project form's cell count is a codebase fact
///         rather than a spec one, so its sentence always says "each of".
///     </para>
/// </remarks>
internal sealed class EachNoun : SelectionNoun
{
    internal EachNoun(IReadOnlyList<Layer> layers)
    {
        Layers = layers;
    }

    internal EachNoun(ProjectSelection projects)
    {
        Projects = projects;
    }

    /// <summary>The declared layer cells, or null for the project form.</summary>
    internal IReadOnlyList<Layer>? Layers { get; }

    /// <summary>The project selection whose projects are the cells, or null for the layer form.</summary>
    internal ProjectSelection? Projects { get; }

    /// <summary>The singular noun one cell is — "layer" or "project" — the word the verb phrases carry.</summary>
    internal string CellWord => Layers is null ? "project" : "layer";

    internal override string Locative => " in " + ReferenceFragment;

    internal override string ReferenceFragment => Layers is { } cells ? LayerFragment(cells) : ProjectFragment();

    // One cell reads as the cell: the identity holds in reference position, so the locative built on it
    // holds too. Layer names are prose rather than identifiers, so they are not backticked — the same
    // choice the single-layer and collapsed-union fragments make.
    private static string LayerFragment(IReadOnlyList<Layer> cells)
    {
        if (cells.Count == 1) return cells[0].Noun.ReferenceFragment;

        List<string> names = cells.Select(cell => cell.Name).ToList();
        return $"each of the {ProseFormat.JoinReferencesAnd(names)} layers";
    }

    // The project selection's own phrase, which already reads as a plural noun ("projects matching `X`",
    // "projects `A` or `B`"), so the family prefixes the quantifier and nothing else.
    private string ProjectFragment()
    {
        return "each of the " + SentenceRenderer.ProjectReference(Projects!);
    }
}

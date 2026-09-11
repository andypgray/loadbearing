using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     Splits a family rule's grandfathered debt by the cell whose law each tolerated violation breaks —
///     what the <c>status</c> row's sub-line and the burndown document's <c>cells</c> array both report.
/// </summary>
/// <remarks>
///     One rule over a family is one row, one baseline and one count, which is what makes a family a family
///     — but the count alone cannot say where the remaining work is, and on the family that prompted this
///     eleven of twelve pairs sat in one cell. The split is derived here on every render and stored nowhere:
///     the baseline file keys pairs, and a cell is a fact about the spec that read them, not about the debt.
///     <para>
///         Both channels come through this one method so a reader cannot find the human line and the
///         document disagreeing about the same run. It answers null — and both channels then say nothing at
///         all — for a rule with no baseline, for a rule whose subject is not a family, and for a family
///         rule with nothing left to burn down.
///     </para>
/// </remarks>
internal static class RatchetCells
{
    /// <summary>
    ///     <paramref name="result" />'s remaining debt per cell, in the order the family declares its cells,
    ///     or null when there is nothing to group.
    /// </summary>
    public static RatchetCellSplit? Of(RuleResult result)
    {
        if (result.Rule.BaselinePath is null) return null;
        if (SelectionWalk.FamilyNoun(result.Rule.Constraint?.Subject) is not { } family) return null;

        Dictionary<string, (int Remaining, int Sites)> held = Held(result.Grandfathered);
        if (held.Count == 0) return null;

        return new RatchetCellSplit(family.CellWord, Ordered(family, held));
    }

    // Only cells that still hold something: a project family can carry every project in a solution, and a
    // burndown that lists the twenty-eight of them at zero is a wall a reader has to search for the two
    // that matter. A violation with no cell contributes nothing — it belongs to no family law.
    private static Dictionary<string, (int Remaining, int Sites)> Held(IReadOnlyList<Violation> grandfathered)
    {
        var held = new Dictionary<string, (int Remaining, int Sites)>(StringComparer.Ordinal);
        foreach (Violation violation in grandfathered)
        {
            if (violation.Cell is not { } cell) continue;

            held.TryGetValue(cell, out (int Remaining, int Sites) running);
            held[cell] = (running.Remaining + 1, running.Sites + violation.Sites.Count);
        }

        return held;
    }

    // Declaration order, which one comparison states for both family forms: a layer family declares its
    // cells in the spec, and a project family's cells are the solution's project list narrowed — and that
    // list arrives ordered by name, so ordinal name order here is the order the cells resolved in rather
    // than a fallback. The name comparison also settles a layer a spec no longer declares, which nothing
    // can currently produce and which would otherwise sort by dictionary order.
    private static IReadOnlyList<RatchetCell> Ordered(EachNoun family, Dictionary<string, (int Remaining, int Sites)> held)
    {
        IReadOnlyList<string> declared = family.Layers is { } layers
            ? layers.Select(layer => layer.Name).ToList()
            : Array.Empty<string>();

        return held
            .OrderBy(cell => Position(declared, cell.Key))
            .ThenBy(cell => cell.Key, StringComparer.Ordinal)
            .Select(cell => new RatchetCell(cell.Key, cell.Value.Remaining, cell.Value.Sites))
            .ToList();
    }

    private static int Position(IReadOnlyList<string> declared, string name)
    {
        for (var index = 0; index < declared.Count; index++)
            if (string.Equals(declared[index], name, StringComparison.Ordinal))
                return index;

        return int.MaxValue;
    }
}

/// <summary>
///     A family rule's remaining debt, split by cell: the word one cell is, and the cells that still hold
///     something in the order the family declares them.
/// </summary>
/// <param name="CellWord">The singular noun one cell is — <c>layer</c> or <c>project</c>.</param>
/// <param name="Cells">The cells still holding grandfathered pairs; never empty.</param>
internal sealed record RatchetCellSplit(string CellWord, IReadOnlyList<RatchetCell> Cells);

/// <summary>One cell's share of the remaining debt.</summary>
/// <param name="Name">The cell's name, as the layer or the project is named.</param>
/// <param name="Remaining">How many tolerated pairs this cell's law holds.</param>
/// <param name="RemainingSites">How many source sites those pairs cover between them.</param>
internal sealed record RatchetCell(string Name, int Remaining, int RemainingSites);

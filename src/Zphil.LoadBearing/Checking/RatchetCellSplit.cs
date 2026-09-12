using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Checking;

/// <summary>
///     A family rule's remaining tolerated debt, grouped by the cell each violation falls in. One rule
///     over a family is one row and one count, and that count says how much is left without saying
///     where; this is where. Read it from <see cref="RuleResult.RatchetCells" />.
/// </summary>
public sealed class RatchetCellSplit
{
    internal RatchetCellSplit(string cellWord, IReadOnlyList<RatchetCell> cells)
    {
        CellWord = cellWord;
        Cells = cells;
    }

    /// <summary>
    ///     Gets the singular noun for one cell — <c>layer</c> or <c>project</c> — according to which
    ///     form declared the family.
    /// </summary>
    public string CellWord { get; }

    /// <summary>
    ///     Gets the cells still holding tolerated violations, in the order the family declares its
    ///     cells. A cell holding nothing is left out, so this is never empty.
    /// </summary>
    public IReadOnlyList<RatchetCell> Cells { get; }

    // Derived on every check and stored nowhere, which is what lets a cell stay a fact about the spec
    // that read the baseline rather than one the baseline has to carry: the file keys pairs, and the
    // same pairs regroup under a respelled family without a recapture.
    internal static RatchetCellSplit? Of(ArchRule rule, IReadOnlyList<Violation> grandfathered)
    {
        if (!rule.IsRatcheted) return null;
        if (SelectionWalk.FamilyNoun(rule.Constraint?.Subject) is not { } family) return null;

        Dictionary<string, (int Remaining, int Sites)> held = Held(grandfathered);
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

/// <summary>One cell's share of a family rule's remaining tolerated debt.</summary>
public sealed class RatchetCell
{
    internal RatchetCell(string name, int remaining, int remainingSites)
    {
        Name = name;
        Remaining = remaining;
        RemainingSites = remainingSites;
    }

    /// <summary>Gets the cell's name, as the layer or the project is named.</summary>
    public string Name { get; }

    /// <summary>Gets how many tolerated violations this cell holds.</summary>
    public int Remaining { get; }

    /// <summary>Gets how many source sites those violations cover between them.</summary>
    public int RemainingSites { get; }
}

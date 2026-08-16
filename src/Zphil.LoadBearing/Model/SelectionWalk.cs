namespace Zphil.LoadBearing.Model;

/// <summary>
///     The canonical walk over every selection a constraint reaches — subject, operands, union parts and
///     <c>Except</c> payloads — held in one place so validation and rendering agree on what a rule
///     talks about.
/// </summary>
/// <remarks>
///     Enumeration order is part of the contract: validation reports its errors in walk order, so a
///     reordering here reorders diagnostics.
/// </remarks>
internal static class SelectionWalk
{
    /// <summary>The constraint's subject tree followed by each operand's tree, in operand order.</summary>
    internal static IEnumerable<Selection> ConstraintSelections(Constraint constraint)
    {
        foreach (Selection selection in ExpandSelection(constraint.Subject)) yield return selection;

        foreach (Selection operand in constraint.Operands)
        foreach (Selection selection in ExpandSelection(operand))
            yield return selection;
    }

    /// <summary>The selection itself, then its union parts, then the payloads of its own <c>Except</c> adjectives.</summary>
    internal static IEnumerable<Selection> ExpandSelection(Selection selection)
    {
        yield return selection;

        // As in SelectionProse: the operands, then this selection's own Except payloads — a union carries
        // adjectives of its own, so AnyOf(a, b).Except(bad) must reach the payload walk.
        if (selection is UnionSelection union)
            foreach (Selection member in union.Parts)
            foreach (Selection nested in ExpandSelection(member))
                yield return nested;

        foreach (SelectionAdjective adjective in selection.Adjectives)
            if (adjective is ExceptAdjective except)
                foreach (Selection nested in ExpandSelection(except.Payload))
                    yield return nested;
    }
}

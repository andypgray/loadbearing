using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotHaveCircularReferences()</c> → "must not have circular references with the others"
///     (GRAMMAR §5.3). The family whose cell graph must carry no circle (§5.1): cells may reference one
///     another, but no strongly connected component of the cell graph may hold more than one cell.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly, on
///     <see cref="MustNotReferenceEachOtherConstraint" />'s precedent: the operand list is empty by
///     construction, because the targets are the subject's own cells. The identity of a violation is the
///     type pair on every arrow inside a component (§4.3), never the cycle. Layer families only —
///     spec-build item 29 refuses a plain subject and a family of projects, so the plain reading below is
///     the total-function fallback rather than a shape any built model carries.
/// </remarks>
internal sealed class MustNotHaveCircularReferencesConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase =>
        "must not have circular references with " + SentenceRenderer.OtherCells(Subject!);
}

using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotReferenceEachOther()</c> → "must not reference the others" (GRAMMAR §5.3). The
///     cross-cell ban over a family subject (§5.1): an owned instance whose far end lies in any other
///     cell, as declared, is a violation. Symmetric, so it has no inbound twin.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly, on
///     <see cref="MustOnlyReferenceItselfConstraint" />'s precedent: the operand list is empty by
///     construction, because the targets are the subject's own cells. The verb needs a family subject —
///     over a plain selection there are no others, which is spec-build item 28, so the plain reading below
///     is the total-function fallback rather than a shape any built model carries.
/// </remarks>
internal sealed class MustNotReferenceEachOtherConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => SentenceRenderer.FamilyCellWord(Subject!) is { } cell
        ? $"must not reference the other {cell}s"
        : "must not reference the others";
}

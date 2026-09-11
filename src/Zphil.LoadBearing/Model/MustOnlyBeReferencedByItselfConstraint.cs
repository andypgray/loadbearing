using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyBeReferencedByItself()</c> → "must be referenced only by itself" (GRAMMAR §5.3). The
///     inbound twin of <see cref="MustOnlyReferenceItselfConstraint" />: on a plain subject the allowed
///     sources are the refined subject — a hermetic set — and on a family subject (§5.1) each cell as
///     declared, which is the modular-monolith law "each module's internals are reached only through its
///     own surface".
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly for the reason its outbound twin does: the operand
///     list is empty by construction rather than by accident. No external-packages caveat is needed —
///     only solution types can be observed referencing (GRAMMAR §4.1) — which is the same asymmetry
///     <see cref="MustOnlyBeReferencedByConstraint" /> carries against
///     <see cref="MustOnlyReferenceConstraint" />. The self phrase is
///     <see cref="SentenceRenderer.SelfReference" />'s, as its twin's is.
/// </remarks>
internal sealed class MustOnlyBeReferencedByItselfConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => $"must be referenced only by {SentenceRenderer.SelfReference(Subject!)}";
}

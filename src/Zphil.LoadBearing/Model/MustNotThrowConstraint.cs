using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotThrow(target, …)</c> → "must not throw {list}" (GRAMMAR §5.3). The ban polarity beside
///     the strict allow-list <see cref="MustOnlyThrowConstraint" />, for the case where the forbidden
///     thrown types are enumerable and the permitted ones are not. Structurally a dependency-shape verb —
///     it carries selection targets on <see cref="Operands" />, exactly like
///     <see cref="MustNotCatchConstraint" /> — so the generic operand/prose/foreign-Arch walks reach it
///     with no special-casing.
/// </summary>
internal sealed class MustNotThrowConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden throw targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not throw " + SentenceRenderer.TargetList(Targets);
}

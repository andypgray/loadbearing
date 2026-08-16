using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotExpose(target, …)</c> → "must not expose {list}" (GRAMMAR §5.3). Forbids a listed
///     target appearing in a public signature position — a return, parameter, or property/field/event
///     type — of an effectively-public member (GRAMMAR §4.9). Structurally a dependency-shape verb — it
///     carries selection targets on <see cref="Constraint.Operands" />, exactly like
///     <see cref="MustNotConstructConstraint" /> — so the generic operand/prose/foreign-Arch walks reach
///     it with no special-casing.
/// </summary>
internal sealed class MustNotExposeConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden exposure targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not expose " + SentenceRenderer.TargetList(Targets);
}

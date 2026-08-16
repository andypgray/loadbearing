using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotInject(target, …)</c> → "must not inject {list}" (GRAMMAR §5.3, §4.7). The
///     captive-dependency verb: a source-level constructor-parameter dependency (primary constructors
///     included). Structurally a dependency-shape verb — it carries selection targets on
///     <see cref="Operands" />, exactly like <see cref="MustNotConstructConstraint" /> — so the generic
///     operand/prose/foreign-Arch walks reach it with no special-casing.
/// </summary>
internal sealed class MustNotInjectConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden injection targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not inject " + SentenceRenderer.TargetList(Targets);
}

namespace Zphil.LoadBearing.Model;

/// <summary>
///     The shared spine of the dependency verbs (GRAMMAR §5.3): a verb whose operand list <em>is</em>
///     its <see cref="Constraint.Operands" /> — the target or source selections it names beside the
///     subject. Sibling of <see cref="MemberConstraint" />, which does the same for the member-subject
///     verbs. Each derived verb keeps its domain-named property (<c>Targets</c>, <c>Sources</c>) as an
///     alias over the one stored list, so the prose reads in the verb's own vocabulary while every
///     operand walk — foreign-<see cref="Arch" /> detection (GRAMMAR §8 item 10), Quarantine
///     desugaring (§7) — reaches it through the one inherited property.
/// </summary>
internal abstract class OperandConstraint(Selection subject, IReadOnlyList<Selection> operands) : Constraint(subject)
{
    internal sealed override IReadOnlyList<Selection> Operands { get; } = operands;
}

using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustBelongTo(membership, …)</c> → "must belong to {list}" (GRAMMAR §5.3). The coverage verb: a
///     subject type no membership names is red. Structurally an operand-carrying verb — it carries
///     selections on <see cref="Constraint.Operands" />, exactly like
///     <see cref="MustNotInjectConstraint" /> — so the generic operand walks (foreign-<see cref="Arch" />
///     validation, the selection walk, Quarantine desugaring) reach it with no special-casing, while
///     evaluation is a per-subject membership test rather than an edge walk.
/// </summary>
internal sealed class MustBelongToConstraint(Selection subject, IReadOnlyList<Selection> memberships) : OperandConstraint(subject, memberships)
{
    /// <summary>The memberships the subject must belong to at least one of.</summary>
    internal IReadOnlyList<Selection> Memberships => Operands;

    internal override string VerbPhrase => "must belong to " + SentenceRenderer.TargetList(Memberships);
}

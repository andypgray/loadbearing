using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyReference(target, …)</c> → "must reference only {list} (external packages are
///     not constrained by this rule)" (GRAMMAR §5.3). The parenthetical states the
///     solution-declared-types complement universe (GRAMMAR §4.1) — honesty is pinned.
/// </summary>
internal sealed class MustOnlyReferenceConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The permitted reference targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    // Plain concatenation, not the closing TargetList overload: the tail is a bracketed parenthetical, which
    // closes an Except the last target left open without a comma of its own (GRAMMAR §6).
    internal override string VerbPhrase
        => "must reference only " + SentenceRenderer.TargetList(Targets) +
           " (external packages are not constrained by this rule)";
}

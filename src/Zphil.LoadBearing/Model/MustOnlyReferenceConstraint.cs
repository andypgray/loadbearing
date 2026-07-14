using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyReference(target, …)</c> → "must reference only {list} (external packages are
///     not constrained by this rule)" (GRAMMAR §5.3). The parenthetical states the
///     solution-declared-types complement universe (GRAMMAR §4.1) — honesty is pinned.
/// </summary>
internal sealed class MustOnlyReferenceConstraint : Constraint
{
    internal MustOnlyReferenceConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The permitted reference targets.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase
        => "must reference only " + SentenceRenderer.TargetList(Targets) +
           " (external packages are not constrained by this rule)";
}
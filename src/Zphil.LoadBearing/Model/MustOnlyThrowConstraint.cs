using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyThrow(target, …)</c> → "must throw only {list}" (GRAMMAR §5.3). STRICT: unlike
///     <see cref="MustOnlyReferenceConstraint" />, there is NO "(external packages are not constrained
///     by this rule)" caveat — MustOnlyThrow constrains external thrown types too, so the caveat's
///     absence IS the strictness rendering. Do not add a parenthetical.
/// </summary>
internal sealed class MustOnlyThrowConstraint : Constraint
{
    internal MustOnlyThrowConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The permitted throw targets.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase => "must throw only " + SentenceRenderer.TargetList(Targets);
}
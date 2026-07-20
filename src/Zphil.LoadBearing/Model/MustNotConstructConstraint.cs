using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotConstruct(target, …)</c> → "must not construct {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotConstructConstraint : Constraint
{
    internal MustNotConstructConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The forbidden construction targets.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase => "must not construct " + SentenceRenderer.TargetList(Targets);
}
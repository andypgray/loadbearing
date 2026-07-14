using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotReference(target, …)</c> → "must not reference {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotReferenceConstraint : Constraint
{
    internal MustNotReferenceConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The forbidden reference targets.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase => "must not reference " + SentenceRenderer.TargetList(Targets);
}
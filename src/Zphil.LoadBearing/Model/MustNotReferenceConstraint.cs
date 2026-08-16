using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotReference(target, …)</c> → "must not reference {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotReferenceConstraint(Selection subject, IReadOnlyList<Selection> targets) : OperandConstraint(subject, targets)
{
    /// <summary>The forbidden reference targets.</summary>
    internal IReadOnlyList<Selection> Targets => Operands;

    internal override string VerbPhrase => "must not reference " + SentenceRenderer.TargetList(Targets);
}

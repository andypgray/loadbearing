using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotBeReferencedBy(source, …)</c> → "must not be referenced by {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotBeReferencedByConstraint(Selection subject, IReadOnlyList<Selection> sources) : OperandConstraint(subject, sources)
{
    /// <summary>The forbidden referencing sources.</summary>
    internal IReadOnlyList<Selection> Sources => Operands;

    internal override string VerbPhrase => "must not be referenced by " + SentenceRenderer.TargetList(Sources);
}

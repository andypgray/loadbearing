using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyBeReferencedBy(source, …)</c> → "must be referenced only by {list}" (GRAMMAR
///     §5.3). No external-packages caveat is needed: only solution types can be observed
///     referencing (GRAMMAR §4.1). This is the containment verb Quarantine desugars to (GRAMMAR §7).
/// </summary>
internal sealed class MustOnlyBeReferencedByConstraint(Selection subject, IReadOnlyList<Selection> sources) : OperandConstraint(subject, sources)
{
    /// <summary>The permitted referencing sources.</summary>
    internal IReadOnlyList<Selection> Sources => Operands;

    internal override string VerbPhrase => "must be referenced only by " + SentenceRenderer.TargetList(Sources);
}

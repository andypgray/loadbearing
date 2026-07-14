using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyBeReferencedBy(source, …)</c> → "must be referenced only by {list}" (GRAMMAR
///     §5.3). No external-packages caveat is needed: only solution types can be observed
///     referencing (GRAMMAR §4.1). This is the containment verb Freeze desugars to (GRAMMAR §7).
/// </summary>
internal sealed class MustOnlyBeReferencedByConstraint : Constraint
{
    internal MustOnlyBeReferencedByConstraint(Selection subject, IReadOnlyList<Selection> sources)
        : base(subject)
    {
        Sources = sources;
    }

    /// <summary>The permitted referencing sources.</summary>
    internal IReadOnlyList<Selection> Sources { get; }

    internal override IReadOnlyList<Selection> Operands => Sources;

    internal override string VerbPhrase => "must be referenced only by " + SentenceRenderer.TargetList(Sources);
}
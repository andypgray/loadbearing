using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustNotBeReferencedBy(source, …)</c> → "must not be referenced by {list}" (GRAMMAR §5.3).</summary>
internal sealed class MustNotBeReferencedByConstraint : Constraint
{
    internal MustNotBeReferencedByConstraint(Selection subject, IReadOnlyList<Selection> sources)
        : base(subject)
    {
        Sources = sources;
    }

    /// <summary>The forbidden referencing sources.</summary>
    internal IReadOnlyList<Selection> Sources { get; }

    internal override IReadOnlyList<Selection> Operands => Sources;

    internal override string VerbPhrase => "must not be referenced by " + SentenceRenderer.TargetList(Sources);
}
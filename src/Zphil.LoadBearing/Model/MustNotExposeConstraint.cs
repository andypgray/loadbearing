using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotExpose(target, …)</c> → "must not expose {list}" (GRAMMAR §5.3). Forbids a listed
///     target appearing in a public signature position — a return, parameter, or property/field/event
///     type — of an effectively-public member (GRAMMAR §4.9). Structurally a dependency-shape verb — it
///     carries selection targets on <see cref="Operands" />, exactly like
///     <see cref="MustNotConstructConstraint" /> — so the generic operand/prose/foreign-Arch walks reach
///     it with no special-casing.
/// </summary>
internal sealed class MustNotExposeConstraint : Constraint
{
    internal MustNotExposeConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The forbidden exposure targets.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase => "must not expose " + SentenceRenderer.TargetList(Targets);
}
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotSwallow(target, …)</c> → "must not swallow {list}" (GRAMMAR §5.3). Forbids <c>catch</c>
///     clauses whose caught type resolves to a listed target, which carry no <c>when</c> filter, and whose
///     block does not end in a <c>throw</c> — the composite of three facts a handler needs to hold a failure
///     and continue. A bare <c>catch</c> counts as <c>System.Exception</c> and counts as unfiltered; a
///     filtered catch and a rethrowing catch are both lawful. Structurally a dependency-shape verb — it
///     carries selection targets on <see cref="Operands" />, exactly like <see cref="MustNotCatchConstraint" />
///     and <see cref="MustNotCatchUnfilteredConstraint" /> — so the generic operand/prose/foreign-Arch walks
///     reach it with no special-casing.
/// </summary>
internal sealed class MustNotSwallowConstraint : Constraint
{
    internal MustNotSwallowConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The catch targets that may not be swallowed.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase => "must not swallow " + SentenceRenderer.TargetList(Targets);
}
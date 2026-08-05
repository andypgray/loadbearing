using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustNotCatchUnfiltered(target, …)</c> → "must not catch {list} without a `when` filter"
///     (GRAMMAR §5.3). Forbids <c>catch</c> clauses whose caught type resolves to a listed target and
///     which carry no <c>when</c> filter; a bare <c>catch</c> counts as <c>System.Exception</c> and
///     counts as unfiltered, while a <c>catch when (…)</c> of any form is filtered. Structurally a
///     dependency-shape verb — it carries selection targets on <see cref="Operands" />, exactly like
///     <see cref="MustNotCatchConstraint" /> — so the generic operand/prose/foreign-Arch walks reach it
///     with no special-casing.
/// </summary>
internal sealed class MustNotCatchUnfilteredConstraint : Constraint
{
    internal MustNotCatchUnfilteredConstraint(Selection subject, IReadOnlyList<Selection> targets)
        : base(subject)
    {
        Targets = targets;
    }

    /// <summary>The catch targets that may not be caught unfiltered.</summary>
    internal IReadOnlyList<Selection> Targets { get; }

    internal override IReadOnlyList<Selection> Operands => Targets;

    internal override string VerbPhrase =>
        "must not catch " + SentenceRenderer.TargetList(Targets) + " without a `when` filter";
}

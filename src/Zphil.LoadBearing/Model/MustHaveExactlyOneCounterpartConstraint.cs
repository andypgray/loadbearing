using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustHaveExactlyOneCounterpart(among:, named:)</c> → "must have exactly one counterpart named
///     {template} among {list}" (GRAMMAR §5.3). The correspondence verb: per subject the template
///     derives a name — every <c>{Name}</c> replaced by the subject's simple name — and exactly one
///     type in the among selection must carry it; zero counterparts and several are both red.
///     Structurally an operand-carrying verb — the among selection rides
///     <see cref="Constraint.Operands" />, exactly like <see cref="MustBelongToConstraint" /> — so the
///     generic operand walks (foreign-<see cref="Arch" /> validation, the selection walk, Quarantine
///     desugaring) reach it with no special-casing, while evaluation is a per-subject name count rather
///     than an edge walk.
/// </summary>
internal sealed class MustHaveExactlyOneCounterpartConstraint(Selection subject, IReadOnlyList<Selection> among, string template)
    : OperandConstraint(subject, among)
{
    /// <summary>The one selection naming where a counterpart may live.</summary>
    internal IReadOnlyList<Selection> Among => Operands;

    /// <summary>The name template; checking replaces every <c>{Name}</c> with the subject's simple name, ordinally.</summary>
    internal string Template { get; } = template;

    /// <summary>The placeholder a template must carry, and the only run <see cref="Derive" /> replaces.</summary>
    // Spelled once because two surfaces must agree on it exactly: checking substitutes it, and spec build
    // refuses a template without it. A placeholder the validator accepted and the checker did not recognize
    // would green every spec and derive a constant name for every subject.
    internal const string NamePlaceholder = "{Name}";

    /// <summary>The name this template derives for one subject, given that subject's simple name.</summary>
    // netstandard2.0's two-string Replace is ordinal by definition — there is no StringComparison overload
    // to spell it with — which is the same match the spec-build placeholder check runs, so a '{name}' typo
    // fails at build rather than deriving a constant name at check time.
    internal string Derive(string simpleName)
    {
        return Template.Replace(NamePlaceholder, simpleName);
    }

    internal override string VerbPhrase =>
        "must have exactly one counterpart named " + ProseFormat.Backtick(Template) + " among " + SentenceRenderer.TargetList(Among);
}

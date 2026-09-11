namespace Zphil.LoadBearing;

/// <summary>
///     A complete rule sentence: a selection plus a <c>Must</c> verb, produced by the verb methods on
///     type, member and project selections and handed to <c>Enforce</c> or <c>Migrate</c>. Nothing on
///     it executes; it is data the check evaluates later.
/// </summary>
// A closed hierarchy with a private protected constructor: no foreign assembly can add a constraint
// node, so every constraint is walkable and renderable. Evaluation is bolted on without reshaping
// these nodes (GRAMMAR §2).
public abstract class Constraint
{
    private protected Constraint(Selection? subject)
    {
        Subject = subject;
    }

    /// <summary>The type selection the constraint is asserted over, or null for a project constraint.</summary>
    /// <remarks>
    ///     Null <em>exactly</em> when the constraint is a project constraint (GRAMMAR §4.10), whose subject
    ///     is a project selection and has no type selection to stand in — where a member constraint hands up
    ///     the type selection its projection was taken from. Every type-side reader of this slot therefore
    ///     either dispatches on the constraint type first (the sentence renderer, the checker) or runs
    ///     downstream of a reader that did. Nothing enforces that by hand: the nullable-flow analysis is the
    ///     audit, so a forgotten dispatch is a build warning rather than a
    ///     <see cref="NullReferenceException" /> in the field.
    /// </remarks>
    internal Selection? Subject { get; }

    /// <summary>The modal verb phrase, lowercase, beginning with "must" (GRAMMAR §5.3).</summary>
    internal abstract string VerbPhrase { get; }

    /// <summary>
    ///     Selection operands beyond the subject — the target/source list of a dependency verb.
    ///     Empty for shape/naming/escape-hatch verbs. Together with the subject these reach every
    ///     selection a rule names, which is what the foreign-selection walk needs (GRAMMAR §8 item 10).
    /// </summary>
    internal virtual IReadOnlyList<Selection> Operands => Array.Empty<Selection>();

    /// <summary>
    ///     Member operands of the member-access verb (<c>MustNotUse</c>) — the banned member targets
    ///     (GRAMMAR §4.5). Walked by validation (GRAMMAR §8 items 11–13); empty for every other verb.
    /// </summary>
    internal virtual IReadOnlyList<Member> MemberOperands => Array.Empty<Member>();
}

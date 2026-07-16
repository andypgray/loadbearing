namespace Zphil.LoadBearing;

/// <summary>
///     A complete, reified constraint sentence — a subject <see cref="Selection" /> plus a modal
///     verb phrase (GRAMMAR §3.1). A closed class hierarchy with a <c>private protected</c>
///     constructor: foreign assemblies cannot introduce constraint nodes, so every constraint is
///     walkable and renderable. Nothing on a constraint executes in Phase 1 — it is data
///     (GRAMMAR §2); Phase 3 bolts on evaluation without reshaping these nodes.
/// </summary>
public abstract class Constraint
{
    private protected Constraint(Selection subject)
    {
        Subject = subject;
    }

    /// <summary>The selection the constraint is asserted over.</summary>
    internal Selection Subject { get; }

    /// <summary>The modal verb phrase, lowercase, beginning with "must" (GRAMMAR §5.3).</summary>
    internal abstract string VerbPhrase { get; }

    /// <summary>
    ///     Selection operands beyond the subject — the target/source list of a dependency verb.
    ///     Empty for shape/naming/escape-hatch verbs. Used by validation to walk every selection
    ///     reachable from a rule (GRAMMAR §8 item 10, foreign selection).
    /// </summary>
    internal virtual IReadOnlyList<Selection> Operands => Array.Empty<Selection>();

    /// <summary>
    ///     Member operands of the member-access verb (<c>MustNotUse</c>) — the banned member targets
    ///     (GRAMMAR §4.5). Walked by validation (GRAMMAR §8 items 11–13); empty for every other verb.
    /// </summary>
    internal virtual IReadOnlyList<Member> MemberOperands => Array.Empty<Member>();
}
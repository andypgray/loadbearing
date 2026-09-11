using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustOnlyReferenceItself()</c> → "must reference only itself (external packages are not
///     constrained by this rule)" (GRAMMAR §5.3). The leaf form of
///     <see cref="MustOnlyReferenceConstraint" />: a graph leaf allows the refined subject and nothing
///     else, which under §4.1's implicit self-allowance leaves the operand list with nothing to hold.
/// </summary>
/// <remarks>
///     Derives from <see cref="Constraint" /> directly rather than from
///     <see cref="OperandConstraint" />, because its operand list is empty by construction rather than by
///     accident — the base's empty <see cref="Constraint.Operands" /> is the whole of what every generic
///     walk needs, and the diagram's zero-operand path lists the rule under the fence instead of drawing
///     an arrow at nothing. The parenthetical is the same honesty pin
///     <see cref="MustOnlyReferenceConstraint" /> carries and states the same complement universe: the
///     verb constrains solution-declared targets, so a leaf may still take a NuGet dependency.
///     <para>
///         The self phrase is <see cref="SentenceRenderer.SelfReference" />'s and agrees with the subject's
///         voice (GRAMMAR §6): over a family subject (§5.1) the allow-set is each cell as declared, so the
///         types voice says "their own layer"; a plain subject says "themselves" in the types voice, the
///         number its "types" head takes, and "itself" only in the collective voice, where it is one layer.
///     </para>
/// </remarks>
internal sealed class MustOnlyReferenceItselfConstraint(Selection subject) : Constraint(subject)
{
    private const string ExternalCaveat = " (external packages are not constrained by this rule)";

    internal override string VerbPhrase =>
        $"must reference only {SentenceRenderer.SelfReference(Subject!)}{ExternalCaveat}";
}

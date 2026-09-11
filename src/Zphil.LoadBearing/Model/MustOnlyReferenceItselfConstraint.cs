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
/// </remarks>
internal sealed class MustOnlyReferenceItselfConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must reference only itself (external packages are not constrained by this rule)";
}

namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustBeSealed()</c> → "must be sealed" (GRAMMAR §5.3).</summary>
internal sealed class MustBeSealedConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be sealed";
}
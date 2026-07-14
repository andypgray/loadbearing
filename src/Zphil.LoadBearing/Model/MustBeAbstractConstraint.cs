namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustBeAbstract()</c> → "must be abstract" (GRAMMAR §5.3).</summary>
internal sealed class MustBeAbstractConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be abstract";
}
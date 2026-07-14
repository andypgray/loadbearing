namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustBePublic()</c> → "must be public" (GRAMMAR §5.3).</summary>
internal sealed class MustBePublicConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be public";
}
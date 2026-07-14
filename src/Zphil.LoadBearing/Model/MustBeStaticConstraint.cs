namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustBeStatic()</c> → "must be static" (GRAMMAR §5.3).</summary>
internal sealed class MustBeStaticConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be static";
}
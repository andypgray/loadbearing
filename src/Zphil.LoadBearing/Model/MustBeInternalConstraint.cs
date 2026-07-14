namespace Zphil.LoadBearing.Model;

/// <summary><c>.MustBeInternal()</c> → "must be internal" (GRAMMAR §5.3).</summary>
internal sealed class MustBeInternalConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be internal";
}
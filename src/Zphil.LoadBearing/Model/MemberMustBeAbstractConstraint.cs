namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustBeAbstract()</c> → "must be abstract" (GRAMMAR §5.7).</summary>
internal sealed class MemberMustBeAbstractConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be abstract";
}
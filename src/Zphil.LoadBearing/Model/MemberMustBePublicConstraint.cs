namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustBePublic()</c> → "must be public" (GRAMMAR §5.7).</summary>
internal sealed class MemberMustBePublicConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be public";
}
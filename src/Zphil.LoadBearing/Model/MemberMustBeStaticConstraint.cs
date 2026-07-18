namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustBeStatic()</c> → "must be static" (GRAMMAR §5.7).</summary>
internal sealed class MemberMustBeStaticConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be static";
}
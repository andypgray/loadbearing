namespace Zphil.LoadBearing.Model;

/// <summary><c>.Methods.MustBeInternal()</c> → "must be internal" (GRAMMAR §5.7).</summary>
internal sealed class MemberMustBeInternalConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be internal";
}
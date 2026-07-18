namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.MustBePrivate()</c> → "must be private" (GRAMMAR §5.7). Member-only vocabulary —
///     there is no type-side twin (a private top-level type is not expressible), deliberate.
/// </summary>
internal sealed class MemberMustBePrivateConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be private";
}
namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.Methods.MustBeVirtual()</c> → "must be virtual" (GRAMMAR §5.7). Member-only vocabulary —
///     there is no type-side twin (virtuality is a member concept), deliberate.
/// </summary>
internal sealed class MemberMustBeVirtualConstraint(MemberSelection subject) : MemberConstraint(subject)
{
    internal override string VerbPhrase => "must be virtual";
}
namespace Zphil.LoadBearing.Model;

/// <summary>
///     <c>.MustBeRegistered()</c> → "must be registered" (GRAMMAR §5.3, §4.7). Membership is
///     <see cref="RegisteredNoun" />'s at any lifetime, so the verb and <c>arch.Registered()</c> cannot
///     drift. It inverts the polarity of that fact's honesty boundary: registration is read from
///     source-visible container registrations, so a registration the extraction cannot see — assembly
///     scanning, keyed overloads, a raw <c>ServiceDescriptor</c>, an extension compiled into a package —
///     makes a correctly registered type a false red rather than a missed one.
/// </summary>
internal sealed class MustBeRegisteredConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be registered";
}

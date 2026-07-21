using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     Types named in a source-visible container registration — <c>arch.Registered(Lifetime)</c>, or
///     <c>arch.Registered()</c> for any lifetime (GRAMMAR §4.7, §5.1). Reifies data only: it carries the
///     optional <see cref="Lifetime" /> filter (<c>null</c> = any lifetime). Membership — the service ∪
///     implementation FQNs of recognized registrations — is resolved model-side in the checker, never
///     denormalized onto a type node.
///     <para>
///         Its fragment ("singleton-registered types" / "registered types") is the noun's <b>head</b>:
///         it renders in reference position and, unlike the type nouns, also replaces the subject head so
///         it survives adjectives (<see cref="SubjectHead" />) — a qualified subject reads
///         "Singleton-registered types, except `X` …", never a false bare "Types, …".
///     </para>
/// </summary>
internal sealed class RegisteredNoun(Lifetime? lifetime) : SelectionNoun
{
    /// <summary>The lifetime filter, or <c>null</c> for any lifetime.</summary>
    internal Lifetime? Lifetime { get; } = lifetime;

    internal override string Locative => string.Empty;

    internal override string ReferenceFragment => ProseFormat.RegisteredFragment(Lifetime);

    internal override string SubjectHead => ProseFormat.RegisteredFragment(Lifetime);
}
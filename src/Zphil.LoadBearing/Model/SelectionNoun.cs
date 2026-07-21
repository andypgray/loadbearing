namespace Zphil.LoadBearing.Model;

/// <summary>
///     The head of a <see cref="Selection" /> — a small closed hierarchy so each noun owns its own
///     prose fragments (GRAMMAR §2 admission rule). <see cref="ReferenceFragment" /> is how the
///     bare noun reads in reference position; <see cref="Locative" /> is the phrase appended after
///     the "types" head in subject (types-voice) assembly (GRAMMAR §6).
/// </summary>
internal abstract class SelectionNoun
{
    /// <summary>The locative phrase appended after the subject head, e.g. <c> in `MyApp.*`</c>.</summary>
    internal abstract string Locative { get; }

    /// <summary>How the bare noun renders as a reference; defaults to the "types" head plus locative.</summary>
    internal virtual string ReferenceFragment => "types" + Locative;

    /// <summary>
    ///     The subject-position head plural. Defaults to "types" — for the type nouns the
    ///     <see cref="Locative" /> carries the distinguishing phrase, so the head stays "types". A noun
    ///     whose fragment IS its whole head (the registration noun, GRAMMAR §5.1) overrides this so the
    ///     head survives adjectives (the <c>OfKind</c> head-substitution mechanic, applied from the noun).
    /// </summary>
    internal virtual string SubjectHead => "types";
}
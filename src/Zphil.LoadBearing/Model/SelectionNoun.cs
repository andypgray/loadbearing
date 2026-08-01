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

    /// <summary>
    ///     The hoisted locative of a homogeneous union — the phrase that follows the one shared head when
    ///     every operand of an <c>arch.AnyOf</c> is this kind of noun, with the operand names or-joined
    ///     (GRAMMAR §6): four project nouns collapse to " in projects `A`, `B`, `C` or `D`" rather than
    ///     or-joining four full phrases. <c>null</c> — the default — means this noun declares no collapse,
    ///     so the union falls back to or-joining each operand. Kept separate from
    ///     <see cref="CollapsedReference" /> so the head stays substitutable (<c>OfKind</c>) and union-level
    ///     adjectives still attach to a head.
    /// </summary>
    /// <param name="group">The nouns of the union's operands, in operand order; all of this noun's type.</param>
    internal virtual string? CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        return null;
    }

    /// <summary>
    ///     How a collapsed homogeneous union reads in reference position; the collapsed twin of
    ///     <see cref="ReferenceFragment" />. Defaults to the shared head plus the hoisted locative — a noun
    ///     whose fragment IS its whole phrase (layers, single types) overrides it. Only reached when
    ///     <see cref="CollapsedLocative" /> is non-null.
    /// </summary>
    internal virtual string CollapsedReference(IReadOnlyList<SelectionNoun> group)
    {
        return "types" + CollapsedLocative(group);
    }
}
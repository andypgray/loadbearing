using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Assembles the deterministic law sentence from a <see cref="Constraint" /> (GRAMMAR §6). The
///     nouns and adjectives own their local fragments; this orchestrates the cross-node concerns:
///     the collective-vs-types voice switch, sentence-final canonicalization of <c>Except</c>/
///     <c>Where</c>, colliding-simple-name qualification in target lists (the shared
///     <see cref="ProseFormat.ResolveTypeDisplays" /> primitive), and capitalization.
/// </summary>
internal static class SentenceRenderer
{
    /// <summary>The full law sentence: <c>{Subject} {verb phrase}.</c></summary>
    internal static string Sentence(Constraint constraint)
    {
        // A member constraint speaks over its member subject ("Methods of types in `MyApp.Web.*` …");
        // every other constraint speaks over its type subject (GRAMMAR §4.6, §6).
        string subject = constraint is MemberConstraint member ? MemberSubject(member.MemberSubject) : Subject(constraint.Subject);
        return subject + " " + constraint.VerbPhrase + ".";
    }

    /// <summary>The capitalized subject phrase for a selection (GRAMMAR §6).</summary>
    internal static string Subject(Selection selection)
    {
        return ProseFormat.Capitalize(Phrase(selection));
    }

    /// <summary>The capitalized member-subject phrase for a member selection (GRAMMAR §4.6, §6).</summary>
    internal static string MemberSubject(MemberSelection selection)
    {
        return ProseFormat.Capitalize(MemberPhrase(selection));
    }

    /// <summary>How a selection reads in reference position (lowercase; joins union members).</summary>
    internal static string Reference(Selection selection)
    {
        if (selection is UnionSelection union) return UnionReference(union);

        // A bare noun (no adjectives) uses its own reference fragment: "the Web layer",
        // "types in `MyApp.*`", "`SqlConnection`". A refined selection falls back to the
        // uncapitalized types-voice phrase.
        return selection.Adjectives.Count == 0 ? selection.Noun.ReferenceFragment : Phrase(selection);
    }

    /// <summary>
    ///     Renders a dependency verb's target/source list, qualifying colliding simple names with
    ///     the minimal distinguishing trailing namespace segments (GRAMMAR §6), then joins with no
    ///     Oxford comma.
    /// </summary>
    internal static string TargetList(IReadOnlyList<Selection> targets)
    {
        var types = new List<Type>();
        foreach (Selection target in targets)
            if (TryBareType(target, out Type type))
                types.Add(type);

        Dictionary<Type, string> display = ProseFormat.ResolveTypeDisplays(types);
        var parts = new List<string>(targets.Count);
        foreach (Selection target in targets)
            parts.Add(TryBareType(target, out Type type)
                ? ProseFormat.Backtick(display[type])
                : Reference(target));

        return ProseFormat.JoinReferences(parts);
    }

    /// <summary>
    ///     Renders a member-access verb's target list (GRAMMAR §4.5, §6): each member as the
    ///     backticked declaring-type dot member — <c>`DateTime.Now`</c>, <c>`Task.Wait()`</c> with
    ///     <c>()</c> appended iff a method — then joins with no Oxford comma. Colliding declaring-type
    ///     simple names widen by the same minimal-trailing-segments rule as the reference list (fed
    ///     through <see cref="ProseFormat.ResolveTypeDisplays" />), including when the member names differ.
    /// </summary>
    internal static string MemberList(IReadOnlyList<Member> members)
    {
        List<Type> declaringTypes = members.Select(member => member.DeclaringType).ToList();
        Dictionary<Type, string> display = ProseFormat.ResolveTypeDisplays(declaringTypes);

        var parts = new List<string>(members.Count);
        foreach (Member member in members)
        {
            string suffix = member.IsMethod ? "()" : string.Empty;
            parts.Add(ProseFormat.Backtick(display[member.DeclaringType] + "." + member.Name + suffix));
        }

        return ProseFormat.JoinReferences(parts);
    }

    /// <summary>The layer definition fragment for the module map: <c>**Domain** — `MyApp.Domain.*`</c>.</summary>
    internal static string LayerDefinition(LayerNoun noun)
    {
        string globs = string.Join(", ", noun.Globs.Select(ProseFormat.Backtick));
        return $"**{noun.Name}** — {globs}";
    }

    private static string Phrase(Selection selection)
    {
        return Phrase(selection, null, null);
    }

    // The types-voice phrase. headOverride and headPrefixOverride carry a union's head and head-prefix
    // adjectives down into an operand of a union that does not collapse, so the kind and authored filters
    // reach the prose instead of being silently dropped from a sentence the checker still applies them to
    // (GRAMMAR §6).
    private static string Phrase(Selection selection, string? headOverride, string? headPrefixOverride)
    {
        if (selection is UnionSelection union) return UnionPhrase(union, headOverride, headPrefixOverride);

        SelectionNoun noun = selection.Noun;
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;

        // Collective voice: a bare layer with no adjectives ("the Domain layer"). Any adjective
        // switches to types voice — the switch is structural, hence deterministic (GRAMMAR §6).
        if (noun is LayerNoun && adjectives.Count == 0 && headOverride is null && headPrefixOverride is null)
            return noun.ReferenceFragment;

        // The head defaults to "types" (the type nouns) but is taken from the noun for a noun whose
        // fragment IS its head — the registration noun — so a qualified Registered subject keeps its
        // qualifier instead of collapsing to a false bare "types" (GRAMMAR §5.1, head truth).
        (string? head, string? headPrefix, string inline, string subjectFinal) =
            Placements(adjectives, headOverride ?? noun.SubjectHead, headPrefixOverride ?? string.Empty);

        return headPrefix + head + noun.Locative + inline + subjectFinal;
    }

    // A union in reference position (GRAMMAR §6). A bare union reads as its collapsed reference when its
    // operands agree — "types in projects `A` or `B`", "the Domain or Web layers", "`X` or `Y`" — and
    // or-joins its operands otherwise. A union carrying its own adjectives has no bare reading, so it
    // falls through to the phrase.
    private static string UnionReference(UnionSelection union)
    {
        if (union.Adjectives.Count > 0) return UnionPhrase(union, null, null);

        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        return nouns is null
            ? ProseFormat.JoinReferences(union.Parts.Select(Reference).ToList())
            : nouns[0].CollapsedReference(nouns);
    }

    // A union in subject (types-voice) position (GRAMMAR §6). A bare union is its reference, so the
    // single-operand identity holds in both positions. Otherwise the union's own adjectives assemble
    // against the collapsed head and locative exactly as they do for a single selection — head
    // substitution, head premodification, inline, sentence-final — or, when the union does not collapse,
    // against the or-joined operand phrases with the head and its prefix distributed into each.
    private static string UnionPhrase(UnionSelection union, string? headOverride, string? headPrefixOverride)
    {
        IReadOnlyList<SelectionAdjective> adjectives = union.Adjectives;
        if (adjectives.Count == 0 && headOverride is null && headPrefixOverride is null) return UnionReference(union);

        (string? head, string? headPrefix, string inline, string subjectFinal) =
            Placements(adjectives, headOverride, headPrefixOverride);

        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        if (nouns is not null)
            return (headPrefix ?? string.Empty) + (head ?? nouns[0].SubjectHead)
                                                + nouns[0].CollapsedLocative(nouns) + inline + subjectFinal;

        List<string> parts = union.Parts.Select(part => Phrase(part, head, headPrefix)).ToList();
        return ProseFormat.JoinReferences(parts) + inline + subjectFinal;
    }

    // Where each adjective lands (GRAMMAR §5.2), accumulated in authoring order: Head and HeadPrefix
    // substitute, Inline and SubjectFinal concatenate. The caller supplies the seeds — Phrase its noun's
    // head and an empty prefix, UnionPhrase the union's nullable overrides — so the two types-voice
    // assemblies cannot disagree about a placement, and a fifth AdjectivePlacement is one edit rather than
    // two silently-diverging ones. MemberPhrase keeps its own variant: its HeadPrefix arm concatenates
    // rather than substitutes, which is a documented divergence and not a copy.
    private static (string? Head, string? HeadPrefix, string Inline, string SubjectFinal) Placements(
        IReadOnlyList<SelectionAdjective> adjectives, string? head, string? headPrefix)
    {
        var inline = string.Empty;
        var subjectFinal = string.Empty;
        foreach (SelectionAdjective adjective in adjectives)
            switch (adjective.Placement)
            {
                case AdjectivePlacement.Head:
                    head = adjective.Fragment;
                    break;
                case AdjectivePlacement.HeadPrefix:
                    headPrefix = adjective.Fragment;
                    break;
                case AdjectivePlacement.Inline:
                    inline += adjective.Fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    subjectFinal += adjective.Fragment;
                    break;
            }

        return (head, headPrefix, inline, subjectFinal);
    }

    // The operand nouns of a union that collapses to one head and locative, or null when it does not
    // (GRAMMAR §6): a single operand (which renders as the bare operand — the identity), an operand
    // carrying adjectives, mixed noun kinds, or a noun kind that declares no collapse all fall back to
    // the or-join.
    private static IReadOnlyList<SelectionNoun>? CollapsibleNouns(UnionSelection union)
    {
        if (union.Parts.Count < 2) return null;

        var nouns = new List<SelectionNoun>(union.Parts.Count);
        foreach (Selection part in union.Parts)
        {
            if (part is UnionSelection || part.Adjectives.Count > 0) return null;
            if (nouns.Count > 0 && part.Noun.GetType() != nouns[0].GetType()) return null;

            nouns.Add(part.Noun);
        }

        return nouns[0].CollapsedLocative(nouns) is null ? null : nouns;
    }

    // Member-subject assembly (GRAMMAR §4.6, §6): head-prefix member adjectives + "{kind-plural} of
    // {selection-reference}" + inline member adjectives in authoring order + the sentence-final member
    // Where. The kind-plural is the projection head; the reference is the underlying type selection in
    // reference position. There is no head-SUBSTITUTION arm: the projection fixes the head.
    private static string MemberPhrase(MemberSelection selection)
    {
        string head = ProseFormat.MemberKindPlural(selection.Kind);
        string reference = Reference(selection.Source);

        var headPrefix = string.Empty;
        var inline = string.Empty;
        var subjectFinal = string.Empty;
        foreach (MemberAdjective adjective in selection.Adjectives)
            switch (adjective.Placement)
            {
                case AdjectivePlacement.HeadPrefix:
                    // NOT a typo for the type side's `headPrefix = adjective.Fragment`: stacked member
                    // prefixes are an INTERSECTION, so a member narrowed by two attribute adjectives carries
                    // both and the sentence must say both. Overwriting would silently drop one and describe
                    // a wider subject than the checker uses. (The type side assigns because its own
                    // head-prefix vocabulary is the single, idempotent `.Authored()`.)
                    headPrefix += adjective.Fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    subjectFinal += adjective.Fragment;
                    break;
                default:
                    inline += adjective.Fragment;
                    break;
            }

        return headPrefix + head + " of " + reference + inline + subjectFinal;
    }

    private static bool TryBareType(Selection selection, out Type type)
    {
        if (selection is not UnionSelection && selection.Adjectives.Count == 0 && selection.Noun is TypeNoun typeNoun)
        {
            type = typeNoun.Type;
            return true;
        }

        type = null!;
        return false;
    }
}

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
        if (selection is UnionSelection union) return ProseFormat.JoinReferences(union.Parts.Select(Reference).ToList());

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

        var display = ProseFormat.ResolveTypeDisplays(types);
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
        var declaringTypes = members.Select(member => member.DeclaringType).ToList();
        var display = ProseFormat.ResolveTypeDisplays(declaringTypes);

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
        SelectionNoun noun = selection.Noun;
        var adjectives = selection.Adjectives;

        // Collective voice: a bare layer with no adjectives ("the Domain layer"). Any adjective
        // switches to types voice — the switch is structural, hence deterministic (GRAMMAR §6).
        if (noun is LayerNoun && adjectives.Count == 0) return noun.ReferenceFragment;

        // The head defaults to "types" (the type nouns) but is taken from the noun for a noun whose
        // fragment IS its head — the registration noun — so a qualified Registered subject keeps its
        // qualifier instead of collapsing to a false bare "types" (GRAMMAR §5.1, head truth).
        string head = noun.SubjectHead;
        var inline = string.Empty;
        var subjectFinal = string.Empty;
        foreach (SelectionAdjective adjective in adjectives)
            switch (adjective.Placement)
            {
                case AdjectivePlacement.Head:
                    head = adjective.Fragment;
                    break;
                case AdjectivePlacement.Inline:
                    inline += adjective.Fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    subjectFinal += adjective.Fragment;
                    break;
            }

        return head + noun.Locative + inline + subjectFinal;
    }

    // Member-subject assembly (GRAMMAR §4.6, §6): "{kind-plural} of {selection-reference}" + inline
    // member adjectives in authoring order + the sentence-final member Where. The kind-plural is the
    // projection head; the reference is the underlying type selection in reference position.
    private static string MemberPhrase(MemberSelection selection)
    {
        string head = ProseFormat.MemberKindPlural(selection.Kind);
        string reference = Reference(selection.Source);

        var inline = string.Empty;
        var subjectFinal = string.Empty;
        foreach (MemberAdjective adjective in selection.Adjectives)
            if (adjective.Placement == AdjectivePlacement.SubjectFinal) subjectFinal += adjective.Fragment;
            else inline += adjective.Fragment;

        return head + " of " + reference + inline + subjectFinal;
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
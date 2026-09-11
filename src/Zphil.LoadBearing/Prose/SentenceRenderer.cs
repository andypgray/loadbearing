using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Assembles the deterministic law sentence from a <see cref="Constraint" /> (GRAMMAR §6). The
///     nouns and adjectives own their local fragments; this orchestrates the cross-node concerns:
///     the collective-vs-types voice switch, sentence-final canonicalization of <c>Except</c>/
///     <c>Where</c>, closing the <c>Except</c> parenthetical with a comma at whatever junction follows
///     it, colliding-simple-name qualification in target lists (the shared
///     <see cref="ProseFormat.ResolveTypeDisplays" /> primitive), and capitalization.
/// </summary>
internal static class SentenceRenderer
{
    /// <summary>The full law sentence: <c>{Subject} {verb phrase}.</c></summary>
    internal static string Sentence(Constraint constraint)
    {
        // One arm per subject stratum (GRAMMAR §4.6, §4.10, §6): a member constraint speaks over its member
        // subject ("Methods of types in `MyApp.Web.*` …"), a project constraint over its project subject
        // ("Packable projects …"), and every other constraint over its type subject. This dispatch is also
        // what makes the bare Subject read below safe — it is null exactly for the project arm above it.
        // The phrase and its openness come out of the same arm, so the two cannot disagree about which
        // stratum was read.
        (string subject, bool endsOpen) = constraint switch
        {
            MemberConstraint member => (MemberSubject(member.MemberSubject), EndsOpen(member.MemberSubject)),
            ProjectConstraint project => (ProjectSubject(project.ProjectSubject), EndsOpen(project.ProjectSubject)),
            _ => (Subject(constraint.Subject!), EndsOpen(constraint.Subject!))
        };

        // The verb is the junction after the subject, so the verb is what closes a subject-final Except.
        return subject + (endsOpen ? ", " : " ") + constraint.VerbPhrase + ".";
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

    /// <summary>The capitalized project-subject phrase for a project selection (GRAMMAR §4.10, §6).</summary>
    internal static string ProjectSubject(ProjectSelection selection)
    {
        return ProseFormat.Capitalize(ProjectPhrase(selection));
    }

    /// <summary>
    ///     How a project selection reads in reference position (GRAMMAR §4.10, §6) — the same phrase,
    ///     uncapitalized, which is what an <c>Except</c> payload renders as inside a sentence.
    /// </summary>
    /// <remarks>
    ///     There is no bare-noun special case here as there is on the type side: a project selection has one
    ///     head and no noun that reads differently as a reference, so subject and reference position differ
    ///     by capitalization alone.
    /// </remarks>
    internal static string ProjectReference(ProjectSelection selection)
    {
        return ProjectPhrase(selection);
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
        return ProseFormat.JoinReferences(ReferenceFragments(targets), ClosesBeforeOr(targets));
    }

    /// <summary>
    ///     One reference fragment per selection, collision-widened as a set: the fragments a list joins,
    ///     before any joining. A boundary's sanctioned surface renders through this so the list a scope
    ///     card prints and the list its sentence names are the same computation (GRAMMAR §6, §7).
    /// </summary>
    internal static IReadOnlyList<string> ReferenceFragments(IReadOnlyList<Selection> selections)
    {
        var types = new List<Type>();
        foreach (Selection selection in selections)
            if (TryBareType(selection, out Type type))
                types.Add(type);

        Dictionary<Type, string> display = ProseFormat.ResolveTypeDisplays(types);
        var parts = new List<string>(selections.Count);
        foreach (Selection selection in selections)
            parts.Add(TryBareType(selection, out Type type)
                ? ProseFormat.Backtick(display[type])
                : Reference(selection));

        return parts;
    }

    /// <summary>
    ///     The same target list followed by a verb-phrase tail, with the last target's open <c>Except</c>
    ///     parenthetical closed before it: "…, except `TimeoutException`, without a `when` filter"
    ///     (GRAMMAR §6). A tail that is a bracketed parenthetical closes the clause on its own and
    ///     concatenates instead.
    /// </summary>
    internal static string TargetList(IReadOnlyList<Selection> targets, string tail)
    {
        return TargetList(targets) + CloseBefore(EndsOpen(targets[targets.Count - 1]), tail);
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

    /// <summary>
    ///     The layer definition fragment for the module map: <c>**Domain** — `MyApp.Domain.*`</c>, and with a
    ///     purpose <c>**Domain** — `MyApp.Domain.*`. {purpose}</c> — prefix-preserving, the purpose verbatim.
    /// </summary>
    internal static string LayerDefinition(LayerNoun noun, string? purpose)
    {
        string globs = string.Join(", ", noun.Globs.Select(ProseFormat.Backtick));
        var fragment = $"**{noun.Name}** — {globs}";
        if (purpose is not null) fragment += $". {purpose}";
        return fragment;
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
        (string? head, string? headPrefix, string inline, string subjectFinal) = Placements(
            adjectives.Select(adjective => (adjective.Placement, adjective.Fragment)),
            headOverride ?? noun.SubjectHead,
            headPrefixOverride ?? string.Empty);

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
            ? ProseFormat.JoinReferences(union.Parts.Select(Reference).ToList(), ClosesBeforeOr(union.Parts))
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

        (string? head, string? headPrefix, string inline, string subjectFinal) = Placements(
            adjectives.Select(adjective => (adjective.Placement, adjective.Fragment)),
            headOverride,
            headPrefixOverride);

        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        if (nouns is not null)
            return (headPrefix ?? string.Empty) + (head ?? nouns[0].SubjectHead)
                                                + nouns[0].CollapsedLocative(nouns) + inline + subjectFinal;

        // The union's own clauses follow the last operand's phrase, so they close an Except it left open.
        List<string> parts = union.Parts.Select(part => Phrase(part, head, headPrefix)).ToList();
        Selection lastPart = union.Parts[union.Parts.Count - 1];
        string clauses = CloseBefore(EndsOpen(lastPart), inline + subjectFinal);
        return ProseFormat.JoinReferences(parts, ClosesBeforeOr(union.Parts)) + clauses;
    }

    // Where each adjective lands (GRAMMAR §5.2, §4.10), accumulated in authoring order: Head and HeadPrefix
    // substitute, Inline and SubjectFinal concatenate. The caller supplies the seeds — Phrase its noun's
    // head and an empty prefix, UnionPhrase the union's nullable overrides, ProjectPhrase the bare plural —
    // so no two subject assemblies can disagree about a placement, and a fifth AdjectivePlacement is one
    // edit rather than one per stratum. It takes (placement, fragment) pairs rather than an adjective list
    // because the strata's adjective hierarchies are deliberately disjoint and share no base: the pairs are
    // the whole of what placement needs from any of them. MemberPhrase keeps its own variant, and that one
    // is a real divergence rather than a copy — its HeadPrefix arm concatenates where these substitute.
    private static (string? Head, string? HeadPrefix, string Inline, string SubjectFinal) Placements(
        IEnumerable<(AdjectivePlacement Placement, string Fragment)> adjectives, string? head, string? headPrefix)
    {
        var inline = string.Empty;
        var subjectFinal = string.Empty;
        foreach ((AdjectivePlacement placement, string fragment) in adjectives)
            switch (placement)
            {
                case AdjectivePlacement.Head:
                    head = fragment;
                    break;
                case AdjectivePlacement.HeadPrefix:
                    headPrefix = fragment;
                    break;
                case AdjectivePlacement.Inline:
                    inline += fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    subjectFinal += fragment;
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

    // Whether a phrase ends inside an Except parenthetical (GRAMMAR §6): its last sentence-final adjective
    // says it opens one — a Where after an Except closes nothing, and is not open either — or, with no
    // clause of its own, it is an or-joined union whose last operand ends open. A collapsed union, a bare
    // noun and an inline-terminated phrase are closed. The composer reads this at every junction where
    // text follows, because the fragment cannot know what follows it.
    private static bool EndsOpen(Selection selection)
    {
        SelectionAdjective? lastClause = selection.Adjectives
            .LastOrDefault(adjective => adjective.Placement == AdjectivePlacement.SubjectFinal);
        if (lastClause is not null) return lastClause.OpensParenthetical;

        bool endsWithInlineClause = selection.Adjectives
            .Any(adjective => adjective.Placement == AdjectivePlacement.Inline);
        if (endsWithInlineClause) return false;

        if (selection is not UnionSelection union || CollapsibleNouns(union) is not null) return false;

        return EndsOpen(union.Parts[union.Parts.Count - 1]);
    }

    // The project stratum's twin. Except and Where are the only sentence-final project adjectives, and the
    // project phrase has no union or bare-noun arm to fall through to.
    private static bool EndsOpen(ProjectSelection selection)
    {
        ProjectAdjective? lastClause = selection.Adjectives
            .LastOrDefault(adjective => adjective.Placement == AdjectivePlacement.SubjectFinal);
        return lastClause is { OpensParenthetical: true };
    }

    // A member subject renders its own inline and sentence-final clauses AFTER the reference, so those
    // clauses close an Except the source left open. Only head prefixes leave the reference at the end.
    private static bool EndsOpen(MemberSelection selection)
    {
        return EndsOpen(selection.Source)
               && selection.Adjectives.All(adjective => adjective.Placement == AdjectivePlacement.HeadPrefix);
    }

    // Comma-closes a following fragment when the phrase before it ended open. Every following fragment opens
    // with a space, so this yields ", returning `Task`", ", whose name …", ", without a `when` filter". An
    // empty following is closed by whatever comes after it — the sentence-final period, the verb — and an
    // Except following an Except already carries the comma.
    private static string CloseBefore(bool endsOpen, string following)
    {
        if (!endsOpen || following.Length == 0 || following[0] == ',') return following;

        return "," + following;
    }

    // The or-joiner's closing decision for a list: only the PENULTIMATE item can end open and still be
    // followed by text, because every item before it is already followed by a comma (GRAMMAR §6).
    private static bool ClosesBeforeOr(IReadOnlyList<Selection> parts)
    {
        return parts.Count >= 2 && EndsOpen(parts[parts.Count - 2]);
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

        return headPrefix + head + " of " + reference + CloseBefore(EndsOpen(selection.Source), inline + subjectFinal);
    }

    // Project-subject assembly (GRAMMAR §4.10, §6): the bare plural "projects" as the head, then the shared
    // placement accumulator. It goes through Placements rather than a loop of its own because the project
    // stratum's placements mean exactly what the type side's do — Named substitutes the head ("project `A`"),
    // Packable premodifies it ("packable "), Matching is an inline reduced relative clause, and Except/Where
    // canonicalize sentence-final. There is no locative: the head carries the whole noun.
    private static string ProjectPhrase(ProjectSelection selection)
    {
        (string? head, string? headPrefix, string inline, string subjectFinal) = Placements(
            selection.Adjectives.Select(adjective => (adjective.Placement, adjective.Fragment)),
            "projects",
            string.Empty);

        return headPrefix + head + inline + subjectFinal;
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

using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Prose;

/// <summary>
///     Assembles the deterministic law sentence from a <see cref="Constraint" /> (GRAMMAR §6). The
///     nouns and adjectives own their local fragments; this orchestrates the cross-node concerns:
///     the collective-vs-types voice switch, sentence-final canonicalization of <c>Except</c>/
///     <c>Where</c>, colliding-simple-name qualification in target lists, and capitalization.
/// </summary>
internal static class SentenceRenderer
{
    /// <summary>The full law sentence: <c>{Subject} {verb phrase}.</c></summary>
    internal static string Sentence(Constraint constraint)
    {
        return Subject(constraint.Subject) + " " + constraint.VerbPhrase + ".";
    }

    /// <summary>The capitalized subject phrase for a selection (GRAMMAR §6).</summary>
    internal static string Subject(Selection selection)
    {
        return ProseFormat.Capitalize(Phrase(selection));
    }

    /// <summary>How a selection reads in reference position (lowercase; joins union members).</summary>
    internal static string Reference(Selection selection)
    {
        if (selection is UnionSelection union) return ProseFormat.JoinReferences(union.Members.Select(Reference).ToList());

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

        var display = ResolveTypeDisplays(types);
        var parts = new List<string>(targets.Count);
        foreach (Selection target in targets)
            parts.Add(TryBareType(target, out Type type)
                ? ProseFormat.Backtick(display[type])
                : Reference(target));

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

        var head = "types";
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

    private static Dictionary<Type, string> ResolveTypeDisplays(IReadOnlyList<Type> types)
    {
        var result = new Dictionary<Type, string>();
        foreach (var group in types.GroupBy(TypeName.Simple))
        {
            var members = group.Distinct().ToList();
            if (members.Count == 1)
            {
                result[members[0]] = TypeName.Simple(members[0]);
                continue;
            }

            // Widen from the simple name outward until every colliding member is distinct.
            int maxDepth = members.Max(TypeName.SegmentDepth);
            int chosen = maxDepth;
            for (var count = 1; count <= maxDepth; count++)
                if (members.Select(t => TypeName.Qualified(t, count)).Distinct().Count() == members.Count)
                {
                    chosen = count;
                    break;
                }

            foreach (Type member in members) result[member] = TypeName.Qualified(member, chosen);
        }

        return result;
    }
}
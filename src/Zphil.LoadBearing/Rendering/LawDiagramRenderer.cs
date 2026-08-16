using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     Composes the architecture law as a Mermaid flowchart — the spec-derived fence beside the
///     codebase-derived one <see cref="GraphDiagramRenderer" /> draws. Pure over an
///     <see cref="ArchitectureModel" /> and the spec name: no extraction, no checker run, no baseline
///     reads, so the fence says what the law is and never what the codebase currently does about it.
///     Output is LF-internal always and carries no timestamp or tool version, so an unchanged spec
///     re-renders to a zero diff.
/// </summary>
/// <remarks>
///     The same house dialect as the survey: <c>flowchart LR</c>, quoted labels,
///     <c>accTitle</c>/<c>accDescr</c>, no colours and no <c>classDef</c>, so structure carries the whole
///     meaning. A solid <c>--x</c> is a forbidden reference, a dotted <c>-.-x</c> is grandfathered debt, a
///     box inside a box is namespace containment, and the doubled box inside a quarantine is its
///     sanctioned surface.
///     <para>
///         Labels carry no counts, for the reason the diagram survey settled: this artifact is committed
///         and drift-gated, and a count moves on nearly every commit. What a drawing cannot place — a
///         verb with no direction, a subject that is not a region of code — is listed by rule ID under
///         the fence instead of being dropped, so the picture narrows and the law does not.
///     </para>
/// </remarks>
public static class LawDiagramRenderer
{
    // Same reasoning as the survey's `p_` prefix (see GraphDiagramRenderer): 19 of Mermaid's flowchart
    // lex rules match a bare word and none is anchored to the start of a statement, and the link rules
    // read a leading o or x as an arrowhead. A prefix puts every emitted ID out of reach of both, and
    // cannot rot as the grammar grows. Places take `s_`, legend rows `l_`, so neither namespace can
    // collide with the other or with the survey's.
    private const string PlaceIdPrefix = "s_";

    private const string LegendIdPrefix = "l_";

    private const string EmptyLawNodeId = PlaceIdPrefix + "none";

    private const string EmptyLawLabel = "(no rules this drawing can place)";

    // The edge vocabulary and the legend rows that explain it, declared together so a label and its
    // legend row cannot drift apart.
    private const string BanArrow = "--x";

    private const string DebtArrowHead = "-.-x";

    private const string ExposeVerb = "expose";

    private const string OnlyVerb = "only";

    private const string BanRowId = "ban";

    private const string BanRowText = "--x = must not reference";

    private const string ExposeRowId = "expose";

    private const string ExposeRowText = "--x expose = must not expose on a public signature";

    private const string OnlyRowId = "only";

    private const string OnlyRowText = "--> only = the only references allowed";

    private const string DebtRowId = "debt";

    private const string DebtRowText = "-.-x grandfathered = Migrate debt, with the existing sites baselined";

    private const string QuarantineRowId = "quarantine";

    private const string QuarantineRowText =
        "Quarantine box = a contained scope; the doubled boxes are its sanctioned surface";

    private const string OutsideRowId = "outside";

    private const string OutsideRowText = "Rounded box = a place named only as the target of a rule";

    private const string NestingRowId = "nesting";

    private const string NestingRowText = "A box inside a box = the inner place is part of the outer";

    /// <summary>
    ///     The law fence's managed-block body: a provenance caption, the fenced Mermaid diagram, and —
    ///     when there is one — the compact list of the rules this drawing could not place in full.
    /// </summary>
    /// <param name="model">The reified spec to draw.</param>
    /// <param name="specName">The spec assembly name, named in the caption and the accessible title.</param>
    public static string Block(ArchitectureModel model, string specName)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNullOrWhiteSpace(specName, nameof(specName));

        var places = new LawPlaces(model.Layers);
        var edges = new List<(LawPlace Source, LawPlace Target, string Arrow)>();
        var unplaced = new List<ArchRule>();

        foreach (ArchRule rule in model.Rules) Walk(rule, places, edges, unplaced);

        places.ResolveNesting();

        var ids = NodeIds(places);
        var lines = new List<string>
        {
            "```mermaid",
            "flowchart LR",
            $"    accTitle: Architecture law: {specName}",
            "    accDescr: The places this spec names, the references it forbids, and the debt it grandfathers.",
            ""
        };
        lines.AddRange(NodeLines(places, ids));

        var edgeLines = EdgeLines(edges, ids);
        if (edgeLines.Count > 0)
        {
            lines.Add("");
            lines.AddRange(edgeLines);
        }

        var legendLines = LegendLines(places, edges);
        if (legendLines.Count > 0)
        {
            lines.Add("");
            lines.AddRange(legendLines);
        }

        lines.Add("```");

        string block = Caption(specName) + "\n\n" + string.Join("\n", lines);
        string list = CompactList(unplaced);
        return list.Length == 0 ? block : block + "\n\n" + list;
    }

    // The provenance/warning caption. Names the spec, because that is where the remedy is: the law is a
    // property of the spec, so a wrong fence is a wrong rule and not a stale render. The full stop where
    // the sibling captions carry an em-dash is deliberate, not copy-paste drift: this caption sits outside
    // the fence, so it counts as prose in the file it lands in, and ARCHITECTURE.md's em-dash allowance
    // (one per thousand words of prose, rounded up) is spent by GraphDiagramRenderer's survey caption
    // above it. Respelling this one to match reds DocHygieneTests.ReaderDoc_StaysWithinProseBudgets and
    // the em-dash-free pin in LawDiagramRendererTests.
    private static string Caption(string specName)
    {
        return $"*Generated by `loadbearing render` from `{specName}`. " +
               "Do not edit between the markers; edit the spec and re-render.*";
    }

    // One rule's contribution to the drawing: its places, its edges, or a line in the compact list.
    private static void Walk(
        ArchRule rule,
        LawPlaces places,
        List<(LawPlace Source, LawPlace Target, string Arrow)> edges,
        List<ArchRule> unplaced)
    {
        // The quarantine arm runs BEFORE the verb switch. A containment rule is a
        // MustOnlyBeReferencedBy, so the switch would happily draw it as an "only" edge — on top of the
        // scope box that already says the same thing, and in the vocabulary of a rule the author never
        // wrote. The ordering is the cure, not a special case inside the switch.
        if (rule.Posture == Posture.Quarantine)
        {
            WalkQuarantine(rule, places, unplaced);
            return;
        }

        if (!LawPlaceClassifier.IsDrawableVerb(rule.Constraint))
        {
            unplaced.Add(rule);
            return;
        }

        Constraint constraint = rule.Constraint!;
        LawPlace? subject = places.Subject(constraint.Subject);
        if (subject is null)
        {
            unplaced.Add(rule);
            return;
        }

        string arrow = Arrow(rule);
        bool inbound = constraint is MustNotBeReferencedByConstraint or MustOnlyBeReferencedByConstraint;
        bool only = constraint is MustOnlyReferenceConstraint or MustOnlyBeReferencedByConstraint;
        var partial = false;
        var drawn = 0;

        foreach (Selection operand in constraint.Operands)
        {
            LawPlace? other = places.Operand(operand);
            if (other is null)
            {
                // A place edge still gets drawn for every operand that has one; the rule is listed as
                // well, so the reader knows the arrows are not the whole of it.
                partial = true;
                continue;
            }

            // An only-verb naming its own subject is the permission for a place to reference itself,
            // which no reader needs an arrow to believe.
            if (only && ReferenceEquals(other, subject)) continue;

            edges.Add(inbound ? (other, subject, arrow) : (subject, other, arrow));
            drawn++;
        }

        // A rule every one of whose operands was skipped drew nothing at all. Listing it is what keeps
        // "drawn or listed" total, rather than leaving a real law invisible on the page.
        if (partial || drawn == 0) unplaced.Add(rule);
    }

    private static void WalkQuarantine(ArchRule rule, LawPlaces places, List<ArchRule> unplaced)
    {
        // The tripwire is a diff-aware touch check rather than a law: no constraint, no sentence, nothing
        // to draw. It is still a rule of its own, so it is listed rather than silently dropped.
        if (rule.Quarantine is not { Role: QuarantineRole.Containment } quarantine)
        {
            unplaced.Add(rule);
            return;
        }

        LawPlace? scope = places.Scope(quarantine.Quarantined, quarantine.ScopeId);
        if (scope is null)
        {
            unplaced.Add(rule);
            return;
        }

        // Boundary order is the spec's order — the first facade is the one the Fix names — and an empty
        // boundary is a hermetic scope, which stays a childless box carrying the same label.
        foreach (Type facade in quarantine.Boundary) places.Facade(facade, scope);
    }

    // The edge's middle token, complete with its pipe label. A Migrate rule draws the same relation it
    // would as an Enforce rule, dotted and labelled as debt; the verb word rides along when the verb has
    // one, so "grandfathered" never has to stand for two different bans.
    private static string Arrow(ArchRule rule)
    {
        string? verb = rule.Constraint switch
        {
            MustNotExposeConstraint => ExposeVerb,
            MustOnlyReferenceConstraint or MustOnlyBeReferencedByConstraint => OnlyVerb,
            _ => null
        };

        if (rule.Posture == Posture.Migrate)
            return $"{DebtArrowHead}|\"{(verb is null ? "grandfathered" : "grandfathered " + verb)}\"|";

        return verb switch
        {
            null => BanArrow,
            OnlyVerb => $"-->|\"{OnlyVerb}\"|",
            _ => $"{BanArrow}|\"{verb}\"|"
        };
    }

    private static Dictionary<LawPlace, string> NodeIds(LawPlaces places)
    {
        var sources = places.Ordered.Select(place => place.IdSource).ToList();
        var ids = MermaidText.UniqueIds(PlaceIdPrefix, sources);

        var map = new Dictionary<LawPlace, string>();
        for (var i = 0; i < places.Ordered.Count; i++) map[places.Ordered[i]] = ids[i];

        return map;
    }

    // Roots in registration order, each followed by what it contains. A law that placed nothing emits the
    // placeholder rather than an empty diagram, so the artifact's shape stays stable — the same
    // convention the survey's empty scope follows.
    private static List<string> NodeLines(LawPlaces places, IReadOnlyDictionary<LawPlace, string> ids)
    {
        var lines = new List<string>();
        foreach (LawPlace root in places.Roots) AppendNode(places, root, ids, 1, lines);

        return lines.Count > 0 ? lines : [$"    {EmptyLawNodeId}[\"{EmptyLawLabel}\"]"];
    }

    private static void AppendNode(
        LawPlaces places, LawPlace place, IReadOnlyDictionary<LawPlace, string> ids, int depth, List<string> lines)
    {
        string indent = new(' ', 4 * depth);
        var children = places.Children(place);
        if (children.Count == 0)
        {
            lines.Add(indent + ids[place] + Shape(place));
            return;
        }

        lines.Add($"{indent}subgraph {ids[place]}[\"{MermaidText.Label(place.Label)}\"]");
        foreach (LawPlace child in children) AppendNode(places, child, ids, depth + 1, lines);

        lines.Add(indent + "end");
    }

    // Shape carries the place's standing: a sanctioned facade is a subroutine box, anything the spec
    // structures — a rule's subject, a declared layer, a place nested inside another — is a rectangle,
    // and what is left is somewhere a rule only ever points at.
    private static string Shape(LawPlace place)
    {
        string label = "\"" + MermaidText.Label(place.Label) + "\"";
        if (place.IsFacade) return $"[[{label}]]";

        return Structural(place) ? $"[{label}]" : $"({label})";
    }

    private static bool Structural(LawPlace place)
    {
        return place.IsSubject || place.IsDeclaredLayer || place.Parent is not null;
    }

    private static List<string> EdgeLines(
        IReadOnlyList<(LawPlace Source, LawPlace Target, string Arrow)> edges,
        IReadOnlyDictionary<LawPlace, string> ids)
    {
        var lines = new List<string>();
        var drawn = new HashSet<string>(StringComparer.Ordinal);
        foreach ((LawPlace source, LawPlace target, string arrow) in edges)
        {
            // Two rules can forbid the same relation in the same words; one arrow says it once.
            var line = $"    {ids[source]} {arrow} {ids[target]}";
            if (drawn.Add(line)) lines.Add(line);
        }

        return lines;
    }

    // Every row is gated on its construct actually appearing in this drawing: a legend that explains a
    // dotted arrow the reader cannot see is teaching them about a feature, not about their architecture.
    private static List<string> LegendLines(
        LawPlaces places, IReadOnlyList<(LawPlace Source, LawPlace Target, string Arrow)> edges)
    {
        var rows = new List<(string Id, string Text)>();
        if (edges.Any(edge => edge.Arrow == BanArrow)) rows.Add((BanRowId, BanRowText));

        if (edges.Any(edge => edge.Arrow.Contains(ExposeVerb))) rows.Add((ExposeRowId, ExposeRowText));

        if (edges.Any(edge => edge.Arrow.Contains(OnlyVerb))) rows.Add((OnlyRowId, OnlyRowText));

        if (edges.Any(edge => edge.Arrow.StartsWith(DebtArrowHead, StringComparison.Ordinal)))
            rows.Add((DebtRowId, DebtRowText));

        if (places.Ordered.Any(place => place.QuarantineScopeId is not null))
            rows.Add((QuarantineRowId, QuarantineRowText));

        if (places.Ordered.Any(place => !place.IsFacade && !Structural(place) && places.Children(place).Count == 0))
            rows.Add((OutsideRowId, OutsideRowText));

        if (places.Ordered.Any(place => !place.IsFacade && place.Parent is not null))
            rows.Add((NestingRowId, NestingRowText));

        if (rows.Count == 0) return [];

        var lines = new List<string> { $"    subgraph {LegendIdPrefix}legend[\"Legend\"]" };
        // Legend text goes out verbatim. It is this renderer's own literal, not a name out of someone's
        // codebase, and it exists to show the arrow glyphs — escaping them would leave the committed
        // artifact reading `--#gt;` where the whole row is the claim that the arrow is `-->`. Quoted label
        // text is a string to Mermaid's lexer, so an arrow inside one is text and never a link.
        lines.AddRange(rows.Select(row => $"        {LegendIdPrefix}{row.Id}[\"{row.Text}\"]"));
        lines.Add("    end");

        return lines;
    }

    // The completeness device: one paragraph after the fence naming, by ID and in authoring order, every
    // rule the drawing could not place in full — a verb with no direction, a subject that is not a region
    // of code, an operand the classifier declined, or the tripwire that draws nothing by design. Omitted
    // entirely when the drawing placed everything, so a fully-drawn law reads as one.
    private static string CompactList(IReadOnlyList<ArchRule> rules)
    {
        if (rules.Count == 0) return string.Empty;

        var entries = rules.Select(rule => $"`{rule.Id}`" + (rule.Posture == Posture.Enforce ? string.Empty : $" ({rule.Posture})"));

        return "Not drawn in full: " + string.Join(", ", entries) +
               ". Expand any of them with `loadbearing explain <rule-id>`.";
    }
}

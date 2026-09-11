using Zphil.LoadBearing.Fluent;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The triage behind the law diagram: which verbs draw as an edge, and which selections land on a
///     place a node can stand for. Everything it declines goes to the compact list under the fence, so a
///     rule this classifier cannot place is still visible — the drawing narrows, the law never does.
/// </summary>
/// <remarks>
///     The honest approximation is noun-level. A noun names a region of the codebase; adjectives narrow
///     which types inside it the rule governs, and no adjective moves the node, because a diagram has no
///     room to say "except these four types" and <c>loadbearing explain</c> carries the exact subject. A
///     union has no single noun (and <see cref="Selection.Noun" /> throws on one), a registration is a
///     lifetime rather than a location, and bare <c>arch.Types</c> is the whole solution — none of the
///     three is a place, so all three go to the list.
/// </remarks>
internal static class LawPlaceClassifier
{
    /// <summary>
    ///     The word an exposure edge's label carries. Shared because the legend row that explains that
    ///     label is gated on the verb rather than on the composed arrow text; the allow-list word needs no
    ///     such constant, because <see cref="DrawableVerb.Only" /> already names that fact.
    /// </summary>
    internal const string ExposeVerb = "expose";

    private const string OnlyVerb = "only";

    /// <summary>
    ///     What the fence draws for a verb, or null when the verb draws nothing. The drawable verbs are
    ///     the four dependency-direction verbs, the leaf form of the outbound allow-list, and the exposure
    ///     verb; every other verb constrains a shape, a name, or a member rather than a relation between
    ///     two places, and an arrow would misrepresent it.
    /// </summary>
    /// <remarks>
    ///     One reading answers the whole drawing — the arrow's direction, the self-edge an allow-list makes
    ///     redundant, the word the label carries, and the legend rows those imply — so a verb can never be
    ///     one thing to the arrow and another to the legend. The leaf form is drawable and draws nothing:
    ///     it names no operand, so it registers its subject's place and then falls to the compact list
    ///     under the fence, which is where its explicitly-self-listing predecessor landed too. Declining it
    ///     here instead would drop the place with it, and a leaf is a node of the graph whether or not an
    ///     arrow leaves it.
    /// </remarks>
    internal static DrawableVerb? Classify(Constraint? constraint)
    {
        return constraint switch
        {
            MustNotReferenceConstraint => new DrawableVerb(inbound: false, only: false, verbWord: null),
            MustNotBeReferencedByConstraint => new DrawableVerb(inbound: true, only: false, verbWord: null),
            MustOnlyReferenceConstraint or MustOnlyReferenceItselfConstraint => new DrawableVerb(inbound: false, only: true, verbWord: OnlyVerb),
            MustOnlyBeReferencedByConstraint => new DrawableVerb(inbound: true, only: true, verbWord: OnlyVerb),
            MustNotExposeConstraint => new DrawableVerb(inbound: false, only: false, verbWord: ExposeVerb),
            _ => null
        };
    }

    /// <summary>Whether the fence can draw an edge for this verb at all.</summary>
    internal static bool IsDrawableVerb(Constraint? constraint)
    {
        return Classify(constraint) is not null;
    }

    /// <summary>
    ///     The place a rule subject stands on, or null when the subject is not place-shaped. A single
    ///     type is not a subject place: a rule anchored on one type is a statement about that type's
    ///     obligations, and drawing the whole architecture around it inverts the scale of the picture.
    /// </summary>
    internal static LawPlace? SubjectPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Subject);
    }

    /// <summary>
    ///     The place an operand stands on, or null when it is not place-shaped. A single type
    ///     <em>is</em> a place here: <c>MustNotReference(typeof(Environment))</c> names exactly one thing
    ///     to point the arrow at, and that is the whole of what the rule forbids.
    /// </summary>
    internal static LawPlace? OperandPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Operand);
    }

    /// <summary>A place standing for one namespace glob, carrying a declared layer's name when one owns that glob.</summary>
    internal static LawPlace FromGlob(string glob, IReadOnlyList<LayerDefinition> layers)
    {
        // Identity collapse: a layer defined by exactly one glob IS that glob, so a rule naming the glob
        // and a rule naming the layer draw one node under the layer's name. A multi-glob layer is a set
        // no single glob equals, so it never collapses.
        LayerDefinition? owner = layers.FirstOrDefault(layer => layer.Globs.Count == 1 && string.Equals(layer.Globs[0], glob, StringComparison.Ordinal));

        return owner is null
            ? new LawPlace(glob, glob, IdSourceOf(glob), [glob], false)
            : new LawPlace(glob, owner.Name, owner.Name, owner.Globs, true);
    }

    /// <summary>
    ///     The place one sanctioned-surface operand stands on, or null when it is not place-shaped. A
    ///     boundary the drawing cannot place is not a boundary the law loses: its containment rule joins
    ///     the compact list under the fence, which is the drawing's totality rule.
    /// </summary>
    internal static LawPlace? FacadePlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return PlaceOf(selection, layers, PlacePosition.Facade);
    }

    /// <summary>A place standing for one sanctioned-surface type inside a quarantined scope.</summary>
    private static LawPlace FromFacade(Type type)
    {
        // The facade's box sits inside the scope's box, which already names the scope, so the simple name
        // is unambiguous where a free-floating type node's would not be.
        return new LawPlace(TypeKey(type), TypeName.Simple(type), TypeName.Simple(type), [], false) { IsFacade = true };
    }

    // Which position of a rule the selection was read in — the one fact that changes what a single type
    // stands for: nothing as a subject, a free-standing node as an operand, and a box inside the scope's
    // box as a sanctioned surface. A region is the same place whichever position names it.
    private enum PlacePosition
    {
        Subject,
        Operand,
        Facade
    }

    private static LawPlace? PlaceOf(Selection? selection, IReadOnlyList<LayerDefinition> layers, PlacePosition position)
    {
        // A union carries no noun head at all, and reading one throws; the guard comes before every
        // other question about the selection.
        if (selection is null or UnionSelection) return null;

        switch (selection.Noun)
        {
            case LayerNoun layer:
                return FromLayer(layer);
            case NamespaceNoun @namespace:
                return FromGlob(@namespace.Glob, layers);
            case ProjectNoun project:
                return new LawPlace("project:" + project.Name, project.Name, project.Name, [], false);
            case TypeNoun type when position == PlacePosition.Facade:
                return FromFacade(type.Type);
            case TypeNoun type when position == PlacePosition.Operand:
                return FromType(type.Type);
            case TypesNoun:
                return FromTypes(selection, layers);
            default:
                return null;
        }
    }

    // A layer's name is its identity in the spec's own vocabulary, so it is the label whatever the globs
    // say. The key follows the collapse rule: one glob and the layer IS that glob.
    private static LawPlace FromLayer(LayerNoun layer)
    {
        string key = layer.Globs.Count == 1 ? layer.Globs[0] : "layer:" + layer.Name;
        return new LawPlace(key, layer.Name, layer.Name, layer.Globs, true);
    }

    // A type node stands alone on the fence with no sentence around it, so it carries the full name: an
    // `Environment` node would leave the reader guessing which one.
    private static LawPlace FromType(Type type)
    {
        string name = Display(type);
        return new LawPlace(TypeKey(type), name, name, [], false);
    }

    // Bare `arch.Types` is the whole solution and places nothing. Exactly one InNamespace adjective
    // narrows it to a region, and that region is a place — the other adjectives (Except, OfKind, Where,
    // …) narrow which types inside it are governed, which is not a question of where. Two InNamespace
    // adjectives are an intersection of regions, and the honest node for that is none.
    private static LawPlace? FromTypes(Selection selection, IReadOnlyList<LayerDefinition> layers)
    {
        List<InNamespaceAdjective> globs = selection.Adjectives.OfType<InNamespaceAdjective>().ToList();
        return globs.Count == 1 ? FromGlob(globs[0].Glob, layers) : null;
    }

    private static string TypeKey(Type type)
    {
        return "type:" + Display(type);
    }

    // The extraction-format name where the type admits one; a pointer or an open construction has no
    // source-level form, and its own simple name is the honest fallback rather than a throw out of a
    // renderer.
    private static string Display(Type type)
    {
        try
        {
            return TypeName.FullDisplay(type);
        }
        catch (UnrepresentableTypeException)
        {
            return TypeName.Simple(type);
        }
    }

    // The subtree operator is noise in an identifier, and the prefix is what a reader recognizes:
    // `Microsoft.Build.*` slugs from `Microsoft.Build`. The operator is decomposed by the matcher that
    // owns it, so a second spelling of it could never leave the slug reading the old one.
    private static string IdSourceOf(string glob)
    {
        NamespacePattern.TryParseSubtree(glob, out string prefix);
        return prefix;
    }

    /// <summary>
    ///     What one drawable verb means to the drawing: which end of the relation the arrow leaves from,
    ///     whether it states an allow-list rather than a ban, and the word its label carries when it has
    ///     one.
    /// </summary>
    internal sealed class DrawableVerb(bool inbound, bool only, string? verbWord)
    {
        /// <summary>Whether the operand is the source of the arrow — the passive voice of a direction verb.</summary>
        internal bool Inbound { get; } = inbound;

        /// <summary>Whether the verb names an allow-list, which is what makes it naming its own subject a no-op.</summary>
        internal bool Only { get; } = only;

        /// <summary>The word the edge label carries, or null for the plain ban that needs none.</summary>
        internal string? VerbWord { get; } = verbWord;
    }
}

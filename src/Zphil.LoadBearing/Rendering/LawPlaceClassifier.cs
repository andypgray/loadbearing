using Zphil.LoadBearing.Model;
using Zphil.LoadBearing.Prose;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The triage behind the law diagram: which verbs draw as an edge, and which selections land on a
///     place a node can stand for. Everything it declines goes to the compact list under the fence, so a
///     rule this classifier cannot place is still visible — the drawing narrows, the law never does.
///     <para>
///         The honest approximation is noun-level. A noun names a region of the codebase; adjectives
///         narrow which types inside it the rule governs, and no adjective moves the node, because a
///         diagram has no room to say "except these four types" and <c>loadbearing explain</c> carries
///         the exact subject. A union has no single noun (and <see cref="Selection.Noun" /> throws on
///         one), a registration is a lifetime rather than a location, and bare <c>arch.Types</c> is the
///         whole solution — none of the three is a place, so all three go to the list.
///     </para>
/// </summary>
internal static class LawPlaceClassifier
{
    /// <summary>
    ///     The five verbs the fence can draw: the four dependency-direction verbs and the exposure verb.
    ///     Every other verb constrains a shape, a name, or a member rather than a relation between two
    ///     places, and an arrow would misrepresent it.
    /// </summary>
    internal static bool IsDrawableVerb(Constraint? constraint)
    {
        return constraint is MustNotReferenceConstraint or MustNotBeReferencedByConstraint
            or MustOnlyReferenceConstraint or MustOnlyBeReferencedByConstraint or MustNotExposeConstraint;
    }

    /// <summary>
    ///     The place a rule subject stands on, or null when the subject is not place-shaped. A single
    ///     type is not a subject place: a rule anchored on one type is a statement about that type's
    ///     obligations, and drawing the whole architecture around it inverts the scale of the picture.
    /// </summary>
    internal static LawPlace? SubjectPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return Classify(selection, layers, false);
    }

    /// <summary>
    ///     The place an operand stands on, or null when it is not place-shaped. A single type
    ///     <em>is</em> a place here: <c>MustNotReference(typeof(Environment))</c> names exactly one thing
    ///     to point the arrow at, and that is the whole of what the rule forbids.
    /// </summary>
    internal static LawPlace? OperandPlace(Selection? selection, IReadOnlyList<LayerDefinition> layers)
    {
        return Classify(selection, layers, true);
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

    /// <summary>A place standing for one sanctioned-surface type inside a quarantined scope.</summary>
    internal static LawPlace FromFacade(Type type)
    {
        // The facade's box sits inside the scope's box, which already names the scope, so the simple name
        // is unambiguous where a free-floating type node's would not be.
        return new LawPlace(TypeKey(type), TypeName.Simple(type), TypeName.Simple(type), [], false) { IsFacade = true };
    }

    private static LawPlace? Classify(Selection? selection, IReadOnlyList<LayerDefinition> layers, bool typeIsAPlace)
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
            case TypeNoun type when typeIsAPlace:
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
        var globs = selection.Adjectives.OfType<InNamespaceAdjective>().ToList();
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
    // `Microsoft.Build.*` slugs from `Microsoft.Build`.
    private static string IdSourceOf(string glob)
    {
        return glob.EndsWith(".*", StringComparison.Ordinal) ? glob.Substring(0, glob.Length - 2) : glob;
    }
}
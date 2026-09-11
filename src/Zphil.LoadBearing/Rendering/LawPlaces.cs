using Zphil.LoadBearing.Hosting;

namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     The set of places one law drawing names, in first-seen order, plus the nesting between them.
///     Registration deduplicates on <see cref="LawPlace.Key" />, so the same place named by four rules is
///     one node that accumulates all four rules' facts.
/// </summary>
/// <remarks>
///     Nesting is namespace containment for everything the spec does not state outright: a place is drawn
///     inside the most specific other place whose glob set strictly covers its own. When two candidate
///     parents are incomparable — neither covers the other — there is no "most specific" answer, and the
///     node stays flat rather than picking one and implying a hierarchy the spec never declared. A layer
///     whose definition refines or unions other places arrives already nested, because its containment is
///     declared rather than inferred, and a project place has no globs to infer anything from.
/// </remarks>
internal sealed class LawPlaces(IReadOnlyList<LayerDefinition> layers)
{
    private readonly Dictionary<string, LawPlace> _byKey = new(StringComparer.Ordinal);

    private readonly List<LawPlace> _ordered = [];

    /// <summary>Every registered place, in the order the rules first named them.</summary>
    internal IReadOnlyList<LawPlace> Ordered => _ordered;

    /// <summary>The places drawn at the top level, in registration order.</summary>
    internal IReadOnlyList<LawPlace> Roots => _ordered.Where(place => place.Parent is null).ToList();

    /// <summary>Registers a rule's subject, or returns null when the subject is not place-shaped.</summary>
    internal LawPlace? Subject(Selection? selection)
    {
        LawPlace? place = Register(LawPlaceClassifier.SubjectPlace(selection, layers));
        if (place is not null) place.IsSubject = true;

        return place;
    }

    /// <summary>Registers one of a verb's operands, or returns null when it is not place-shaped.</summary>
    internal LawPlace? Operand(Selection? selection)
    {
        return Register(LawPlaceClassifier.OperandPlace(selection, layers));
    }

    /// <summary>Registers a quarantined scope's interior as a place carrying the scope's identity.</summary>
    internal LawPlace? Scope(Selection? selection, string scopeId)
    {
        LawPlace? place = Subject(selection);
        if (place is not null) place.QuarantineScopeId = scopeId;

        return place;
    }

    /// <summary>
    ///     Registers one sanctioned-surface operand as a child of its scope's place, or returns null when
    ///     the operand is not place-shaped. A region named in facade position keeps its own place and gains
    ///     the facade standing, so a namespace two rules name is still one node.
    /// </summary>
    internal LawPlace? Facade(Selection? selection, LawPlace scope)
    {
        LawPlace? place = Register(LawPlaceClassifier.FacadePlace(selection, layers));
        if (place is null) return null;

        place.IsFacade = true;
        place.Parent ??= scope;
        return place;
    }

    /// <summary>The places drawn inside <paramref name="place" />, in registration order.</summary>
    internal IReadOnlyList<LawPlace> Children(LawPlace place)
    {
        return _ordered.Where(candidate => ReferenceEquals(candidate.Parent, place)).ToList();
    }

    /// <summary>
    ///     Assigns each unparented place its most specific strict container, once every place is known.
    ///     Containment is a strict partial order, so the parent chains it builds always ascend and can
    ///     never close a cycle.
    /// </summary>
    internal void ResolveNesting()
    {
        foreach (LawPlace place in _ordered)
        {
            if (place.Parent is not null || place.Globs.Count == 0) continue;

            List<LawPlace> containers = _ordered
                .Where(other => !ReferenceEquals(other, place) && other.Globs.Count > 0 && StrictlyContains(other, place))
                .ToList();

            // The most specific container is the one every other container also covers. Two containers
            // that cover neither each other nor a common candidate leave no such place, and the node
            // stays where it is.
            place.Parent = containers.FirstOrDefault(candidate => containers.All(other => ReferenceEquals(other, candidate) || Contains(other, candidate)));
        }
    }

    private static bool StrictlyContains(LawPlace outer, LawPlace inner)
    {
        return Contains(outer, inner) && !Contains(inner, outer);
    }

    private static bool Contains(LawPlace outer, LawPlace inner)
    {
        return inner.Globs.All(innerGlob => outer.Globs.Any(outerGlob => NamespaceContainment.Implies(innerGlob, outerGlob)));
    }

    private LawPlace? Register(LawPlace? place)
    {
        return place is null ? null : Registered(place);
    }

    // Registration proper, and the one place structural containment is resolved to registered instances: a
    // place is drawn inside another only if the two nodes on the page are the ones the rules registered.
    private LawPlace Registered(LawPlace place)
    {
        if (_byKey.TryGetValue(place.Key, out LawPlace existing)) return existing;

        // A structural parent is drawn whether or not a rule names it — a layer defined as a refinement of
        // another says where it sits, and the box has to be on the page for the child to sit inside it.
        // Registering it first also puts a container ahead of its content in registration order.
        if (place.Parent is { } parent) place.Parent = Registered(parent);

        _byKey.Add(place.Key, place);
        _ordered.Add(place);

        // A structural child keeps a parent it already has, so a place two definitions claim is drawn once,
        // inside the first box that claimed it — the same first-wins rule registration itself follows.
        foreach (LawPlace child in place.StructuralChildren) Registered(child).Parent ??= place;

        return place;
    }
}

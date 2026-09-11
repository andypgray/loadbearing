namespace Zphil.LoadBearing.Rendering;

/// <summary>
///     One node of the law diagram: a place the spec names, identified by a
///     <see cref="Key" /> so two rules naming the same place draw one node.
/// </summary>
/// <remarks>
///     The mutable flags are accumulated as the rules are walked: a place is not yet known to be a
///     subject, a quarantined scope, or a child when the first rule mints it.
/// </remarks>
internal sealed class LawPlace
{
    internal LawPlace(string key, string label, string idSource, IReadOnlyList<string> globs, bool declaredLayer)
    {
        Key = key;
        BaseLabel = label;
        IdSource = idSource;
        Globs = globs;
        IsDeclaredLayer = declaredLayer;
    }

    /// <summary>The dedupe identity — a single-glob layer and its glob share one, which is the identity collapse.</summary>
    internal string Key { get; }

    /// <summary>The name the node ID is slugged from; distinct from the label so an ID stays readable.</summary>
    internal string IdSource { get; }

    /// <summary>The namespace globs this place covers, for containment nesting; empty for a project or a type.</summary>
    internal IReadOnlyList<string> Globs { get; }

    /// <summary>Whether the spec declares this place as a layer, which is what earns the layer name as a label.</summary>
    internal bool IsDeclaredLayer { get; }

    /// <summary>Whether any rule takes this place as its subject.</summary>
    internal bool IsSubject { get; set; }

    /// <summary>Whether this place is a quarantined scope's sanctioned surface type.</summary>
    internal bool IsFacade { get; set; }

    /// <summary>The scope this place is the quarantined interior of, or null.</summary>
    internal string? QuarantineScopeId { get; set; }

    /// <summary>The place this one is drawn inside, or null when it is drawn at the top level.</summary>
    internal LawPlace? Parent { get; set; }

    /// <summary>
    ///     The places this one contains by construction rather than by namespace containment — the operands
    ///     of a layer defined as a union of places, empty for everything else.
    /// </summary>
    /// <remarks>
    ///     Carried here rather than resolved at classification because a child has to be the
    ///     <em>registered</em> instance of its place to be drawn inside anything;
    ///     <see cref="LawPlaces" /> is where that identity lives.
    /// </remarks>
    internal IReadOnlyList<LawPlace> StructuralChildren { get; set; } = [];

    /// <summary>
    ///     The displayed label: a quarantined scope announces itself, and everything else keeps the name
    ///     it was classified under — the layer name, the glob, the project, or the type.
    /// </summary>
    internal string Label => QuarantineScopeId is { } scopeId ? "Quarantine: " + scopeId : BaseLabel;

    private string BaseLabel { get; }
}

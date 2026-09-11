using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     A set of members the spec talks about: the methods, properties, fields and events the selected
///     types declare, reached with <c>.Members</c>, <c>.Methods</c>, <c>.Properties</c>,
///     <c>.Fields</c> or <c>.Events</c> on a <see cref="Selection" />. Narrow it with adjectives such
///     as <c>WithSuffix</c>, <c>WithPrefix</c>, <c>WithNameMatching</c>, <c>AttributedWith</c>,
///     <c>ThatAreStatic</c> or <c>Where</c>, each of which returns a new selection of the same kind so
///     that a projection's own calls stay reachable; finish it with a verb such as
///     <c>MustHaveSuffix</c>, <c>MustBePublic</c>, <c>MustBeStatic</c> or <c>MustBeAttributedWith</c>,
///     which yields the <see cref="Constraint" /> a rule takes. What counts as a member is what the
///     type declares: property and event accessors, constructors, operators, indexers, finalizers and
///     explicit interface implementations are not members here, and enum and delegate types contribute
///     none. A rule whose subject matches no member fails saying so, and each violation is reported at
///     the member's own declaration. Immutable and reusable: assign one to a variable and use it in as
///     many rules as you like.
/// </summary>
// A closed hierarchy: the constructor is private protected, so no foreign assembly can add a node,
// and it is disjoint from Selection and ProjectSelection so the shared adjective and verb names
// bind by receiver type rather than by overload resolution (GRAMMAR §3.2). The three slots below —
// Source, Kind, Adjectives — are exactly what member-subject sentence assembly reads (§6); the
// adjectives ship as generic self-type extensions that clone through Rebuild, which is what keeps a
// concrete projection's kind-only vocabulary reachable after one.
public abstract class MemberSelection
{
    private protected MemberSelection(Selection source, MemberKindFilter kind, IReadOnlyList<MemberAdjective> adjectives)
    {
        Source = source;
        Kind = kind;
        Adjectives = adjectives;
    }

    /// <summary>The underlying type selection the members are drawn from; the inherited constraint subject (GRAMMAR §4.6).</summary>
    internal Selection Source { get; }

    /// <summary>Which member kind the projection selected.</summary>
    internal MemberKindFilter Kind { get; }

    /// <summary>The ordered member-adjective refinements applied to the projected member set.</summary>
    internal IReadOnlyList<MemberAdjective> Adjectives { get; }

    /// <summary>
    ///     The covariant clone: each concrete member-selection type returns its own type carrying the
    ///     new adjective list, so an adjective extension preserves <see cref="MethodSelection" /> (and
    ///     keeps <c>.Returning</c> reachable) in any order.
    /// </summary>
    private protected abstract MemberSelection Rebuild(IReadOnlyList<MemberAdjective> adjectives);

    /// <summary>Appends one adjective and clones (the assembly-internal entry the adjective extensions call).</summary>
    internal MemberSelection Refined(MemberAdjective adjective)
    {
        var adjectives = new List<MemberAdjective>(Adjectives) { adjective };
        return Rebuild(adjectives);
    }
}

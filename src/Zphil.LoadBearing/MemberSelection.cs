using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     An immutable, reusable selection over the declared <em>members</em> of a type selection
///     (GRAMMAR §4.6). Minted by the projections on <see cref="Selection" />
///     (<c>.Members</c>/<c>.Methods</c>/<c>.Properties</c>/<c>.Fields</c>/<c>.Events</c>). A closed
///     class hierarchy with a <c>private protected</c> constructor, <b>disjoint</b> from
///     <see cref="Selection" /> — that disjointness is what keeps the shared adjective/verb names
///     (<c>WithSuffix</c>, <c>MustBeStatic</c>, …) from colliding on overload resolution: a call binds
///     to the type-side or member-side vocabulary purely by receiver type.
/// </summary>
/// <remarks>
///     A member selection is a composite: the underlying <see cref="Source" /> type selection, the
///     <see cref="Kind" /> filter that named it, and an ordered list of <see cref="MemberAdjective" />
///     refinements (GRAMMAR §6 member-subject assembly reads exactly these three). Member modal-verb
///     extensions turn it into a terminal <see cref="Constraint" />; member adjectives are generic
///     self-type extensions that clone via <see cref="Rebuild" />, so a chain preserves its concrete
///     type (<c>.Methods.Returning(...)</c> stays a <see cref="MethodSelection" />).
/// </remarks>
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
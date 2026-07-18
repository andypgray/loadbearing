namespace Zphil.LoadBearing.Model;

/// <summary>
///     Which declared members a projection selects (GRAMMAR §4.6): the head that the
///     <c>.Members</c>/<c>.Methods</c>/<c>.Properties</c>/<c>.Fields</c>/<c>.Events</c> properties
///     stamp onto a <see cref="MemberSelection" />. Distinct from <see cref="Codebase.MemberKind" />
///     (the actual kind of an inventoried member): this carries the extra <see cref="Any" /> case for
///     the unrestricted <c>.Members</c> projection.
/// </summary>
internal enum MemberKindFilter
{
    /// <summary>All member kinds — the <c>.Members</c> projection.</summary>
    Any,

    /// <summary>Methods only — the <c>.Methods</c> projection.</summary>
    Method,

    /// <summary>Properties only — the <c>.Properties</c> projection.</summary>
    Property,

    /// <summary>Fields only — the <c>.Fields</c> projection.</summary>
    Field,

    /// <summary>Events only — the <c>.Events</c> projection.</summary>
    Event
}
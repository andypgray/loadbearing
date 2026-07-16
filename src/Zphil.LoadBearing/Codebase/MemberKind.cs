namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     The kinds of member a member-use edge can target (GRAMMAR §4.5). This is the inventory the
///     syntax walk records a use against — LoadBearing's own enum, never Roslyn's <c>SymbolKind</c>.
///     Accessors do not appear: a property/event accessor folds into its declaring
///     <see cref="Property" />/<see cref="Event" /> at extraction, so <c>obj.P</c> reads and
///     <c>obj.P = x</c> writes are one <see cref="Property" /> edge, not two accessor-method edges.
/// </summary>
public enum MemberKind
{
    /// <summary>A method (ordinary or a reduced extension normalized to its declaring static method).</summary>
    Method,

    /// <summary>A property (both accessors fold here).</summary>
    Property,

    /// <summary>A field, including an enum member.</summary>
    Field,

    /// <summary>An event (both accessors fold here).</summary>
    Event
}
namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     Which kind of member a <see cref="MemberReference" /> names. An accessor never appears: a property's
///     or an event's accessor is recorded as the property or event itself, so <c>obj.P</c> and
///     <c>obj.P = x</c> are both one property use rather than two calls.
/// </summary>
// LoadBearing's own enum, never Roslyn's SymbolKind: Core is netstandard2.0 and references no Roslyn.
// The accessor fold happens at extraction (GRAMMAR §4.5), so no reader has to redo it.
public enum MemberKind
{
    /// <summary>
    ///     A method. A call written in extension-method form is recorded against the static method that
    ///     declares it.
    /// </summary>
    Method,

    /// <summary>A property. Both of its accessors are recorded here.</summary>
    Property,

    /// <summary>A field, an enum member included.</summary>
    Field,

    /// <summary>An event. Both of its accessors are recorded here.</summary>
    Event
}

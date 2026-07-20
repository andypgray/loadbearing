using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Model;

/// <summary>
///     A rule or scope anchor registered on an <see cref="Arch" /> the moment <c>Rule</c>/<c>Scope</c>
///     is called (register-on-anchor, GRAMMAR §3.2). Held in authoring order so the read model can
///     preserve rule order with scope children expanded in place.
/// </summary>
internal abstract class Registration(string id)
{
    /// <summary>The declared anchor ID.</summary>
    internal string Id { get; } = id;

    /// <summary>
    ///     The spec-source position of the <c>Rule</c>/<c>Scope</c> call that registered this anchor,
    ///     captured via caller info (GRAMMAR §8), or null when none was captured. Rides the registration
    ///     so every rule/scope-attributed validation error renders at the offending statement.
    /// </summary>
    internal SpecSourceLocation? Location { get; set; }
}
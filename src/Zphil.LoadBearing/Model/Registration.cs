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
}
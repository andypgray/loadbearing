namespace Zphil.LoadBearing.Internal;

/// <summary>
///     What "single-line" means for every authored prose value the product carries — a spec trailer, a
///     baseline attribution — held in one place on <see cref="Rendering.Plurals" />'s precedent, so a
///     refinement lands once rather than being hunted for across three assemblies.
/// </summary>
/// <remarks>
///     Either terminator, so a value carrying a bare CR is caught as well as one carrying a newline.
///     Blankness is deliberately not folded in: the spec catalog reports blank under its own code, and the
///     two baseline readers say "blank or multi-line" in one breath because their channel has one message.
/// </remarks>
internal static class SingleLineProse
{
    internal static bool IsMultiLine(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }
}

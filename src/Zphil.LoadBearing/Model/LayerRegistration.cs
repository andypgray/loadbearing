namespace Zphil.LoadBearing.Model;

/// <summary>
///     Mutable backing state for a declared layer: the minted noun and every <c>Purpose</c> supplied
///     (at most one is valid) — a list, so a repeated trailer is detectable by validation (§8 item 6).
/// </summary>
/// <remarks>
///     Not a <see cref="Registration" />: a layer has no ID and captures no spec-source location, which is
///     why every layer error is spec-wide and named by layer.
/// </remarks>
internal sealed class LayerRegistration(LayerNoun noun)
{
    /// <summary>The noun the layer's <see cref="Layer" /> selection carries.</summary>
    internal LayerNoun Noun { get; } = noun;

    /// <summary>Every <c>Purpose</c> supplied (at most one is valid).</summary>
    internal List<string> Purposes { get; } = [];
}

namespace Zphil.LoadBearing.Hosting;

/// <summary>
///     The finished architecture model: every rule and every layer a spec declared, in one object to
///     read from. Returned by <c>ArchModelBuilder.Build</c>, which hands one back only after the spec
///     passes validation, so nothing here is half-formed and no default is left to fill in.
/// </summary>
public sealed class ArchitectureModel
{
    internal ArchitectureModel(IReadOnlyList<ArchRule> rules, IReadOnlyList<LayerDefinition> layers)
    {
        Rules = rules;
        Layers = layers;
    }

    /// <summary>
    ///     Gets every rule, in the order the spec declared them, with the rules each scope contributes — a
    ///     quarantine's containment then its tripwire, a caution's tripwire alone — standing where the scope
    ///     itself was declared.
    /// </summary>
    public IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>Gets every layer, in the order the spec declared them.</summary>
    public IReadOnlyList<LayerDefinition> Layers { get; }
}

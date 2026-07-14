namespace Zphil.LoadBearing.Model;

/// <summary>
///     A named layer defined by one or more namespace globs —
///     <c>
///         arch.Layer("Domain",
///         "MyApp.Domain.*")
///     </c>
///     . Reference fragment: "the Domain layer" (GRAMMAR §5.1). A bare
///     <see cref="Layer" /> subject with zero adjectives speaks in the collective voice (GRAMMAR §6).
/// </summary>
internal sealed class LayerNoun(string name, IReadOnlyList<string> globs) : SelectionNoun
{
    /// <summary>The layer name.</summary>
    internal string Name { get; } = name;

    /// <summary>The namespace globs that define the layer (at least one).</summary>
    internal IReadOnlyList<string> Globs { get; } = globs;

    internal override string Locative => $" in the {Name} layer";

    internal override string ReferenceFragment => $"the {Name} layer";
}
using Zphil.LoadBearing.Prose;

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

    internal override string? CollapsedLocative(IReadOnlyList<SelectionNoun> group)
    {
        return $" in the {LayerNames(group)} layers";
    }

    internal override string CollapsedReference(IReadOnlyList<SelectionNoun> group)
    {
        return $"the {LayerNames(group)} layers";
    }

    // Bare layer names, or-joined: a layer name is prose, not an identifier, so it is not backticked —
    // the same choice the single-layer fragment makes.
    private static string LayerNames(IReadOnlyList<SelectionNoun> group)
    {
        return ProseFormat.JoinReferences(group.Select(noun => ((LayerNoun)noun).Name).ToList());
    }
}
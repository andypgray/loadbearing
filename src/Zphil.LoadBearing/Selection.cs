using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     An immutable, reusable set of types the spec talks about (GRAMMAR §3). A closed class
///     hierarchy: the constructor is <c>private protected</c>, so foreign assemblies cannot
///     introduce selection nodes, and every node in the model is walkable and renderable by
///     construction. The growing vocabulary ships as public extension methods
///     (<see cref="SelectionAdjectives" />) that build the internal nodes.
/// </summary>
/// <remarks>
///     A selection is a composite: a single <see cref="SelectionNoun" /> plus an ordered list of
///     <see cref="SelectionAdjective" /> refinements (GRAMMAR §6 subject assembly reads exactly
///     these two). Modal-verb extensions turn a selection into a terminal <see cref="Constraint" />.
/// </remarks>
public abstract class Selection
{
    private protected Selection(Arch owner)
    {
        Owner = owner;
    }

    /// <summary>The <see cref="Arch" /> this selection was minted on (GRAMMAR §3.2 fresh-instance contract).</summary>
    internal Arch Owner { get; }

    /// <summary>The noun head of the selection.</summary>
    internal abstract SelectionNoun Noun { get; }

    /// <summary>The ordered adjective refinements applied to the noun.</summary>
    internal abstract IReadOnlyList<SelectionAdjective> Adjectives { get; }
}
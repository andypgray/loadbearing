namespace Zphil.LoadBearing.Model;

/// <summary>
///     The union of one or more selections — <c>arch.AnyOf(a, b, …)</c> (GRAMMAR §5.1) — and the
///     payload the Quarantine containment desugaring feeds into an <c>Except</c> as <c>sel ∪ F</c>
///     (§7). Adjectives apply to the union, not through it: <c>AnyOf(a, b).Except(c)</c> is
///     <c>(a ∪ b) − c</c>, so the union carries its own adjective list and the evaluator applies it
///     after the set union.
///     <para>
///         A union has no single noun — <see cref="Noun" /> throws by design. It renders through its
///         own §6 arm instead: collapsed to one head and locative when every operand shares a noun kind
///         that declares a collapse, or-joined operand by operand otherwise.
///     </para>
/// </summary>
internal sealed class UnionSelection : Selection
{
    internal UnionSelection(Arch owner, IReadOnlyList<Selection> parts)
        : this(owner, parts, Array.Empty<SelectionAdjective>())
    {
    }

    private UnionSelection(Arch owner, IReadOnlyList<Selection> parts, IReadOnlyList<SelectionAdjective> adjectives)
        : base(owner)
    {
        Parts = parts;
        Adjectives = adjectives;
    }

    /// <summary>The unioned selections, in order (named <c>Parts</c> to stay clear of the <c>.Members</c> projection, §4.6).</summary>
    internal IReadOnlyList<Selection> Parts { get; }

    internal override SelectionNoun Noun
        => throw new InvalidOperationException("A union selection has no single noun; render it in reference position.");

    internal override IReadOnlyList<SelectionAdjective> Adjectives { get; }

    /// <summary>
    ///     Mints a union, flattening nested unions at mint so prose and evaluation both read one leaf
    ///     list: <c>AnyOf(AnyOf(a, b), c)</c> ≡ <c>AnyOf(a, b, c)</c>. Only an <em>adjective-free</em>
    ///     union operand flattens — an inner union carrying adjectives is a narrowed set of its own, so
    ///     it stays a leaf.
    /// </summary>
    internal static UnionSelection Create(Arch owner, IReadOnlyList<Selection> parts)
    {
        var flattened = new List<Selection>(parts.Count);
        foreach (Selection part in parts)
            if (part is UnionSelection { Adjectives.Count: 0 } nested) flattened.AddRange(nested.Parts);
            else flattened.Add(part);

        return new UnionSelection(owner, flattened);
    }

    /// <summary>This union with its adjective list replaced — the union arm of the adjective append (§5.2).</summary>
    internal UnionSelection WithAdjectives(IReadOnlyList<SelectionAdjective> adjectives)
    {
        return new UnionSelection(Owner, Parts, adjectives);
    }
}
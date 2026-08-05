namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed catch edge <c>Source → Caught</c>: <see cref="Source" />'s declaration source has a
///     <c>catch</c> clause whose caught type resolves to <see cref="Caught" /> (GRAMMAR §4.8). A typed
///     <c>catch (IOException e)</c> records <see cref="Caught" /> as <c>IOException</c>; a bare <c>catch</c>
///     records <c>System.Exception</c> — nothing in source names the type, so a bare <c>catch</c> mints no
///     type-level <see cref="ReferenceEdge" /> (the caught channel only). Constructed generics normalize to
///     their open definition (§4.1). <see cref="Sites" /> lists the distinct <c>file:line</c> positions of
///     the <c>catch</c> clauses, deduped by (file, line), <see cref="UnfilteredSites" /> the subset of
///     those whose clause spells no <c>when</c> filter, and <see cref="SwallowingSites" /> the subset of
///     <em>those</em> whose block does not end in a <c>throw</c>.
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Caught" /> are the same <see cref="TypeNode" /> instances held by
///     <see cref="CodebaseModel.Types" /> (reference equality, not just name equality). Self-catch (a type
///     catching itself) is never produced — the catch analog of the reference-edge self-drop (§4.1). A
///     rethrowing catch still mints (edges are facts). A <c>when</c> filter never suppresses the edge; its
///     contents mint their own ordinary type/member edges. What a filter does do is keep its site out of
///     <see cref="UnfilteredSites" /> — an explicitly separate fact recorded beside <see cref="Sites" />, which
///     it is always a subset of. Filter presence there is <em>syntactic</em>: a <c>when (true)</c> is a filter,
///     because the axis records what the source spells and never judges what a filter tests. A bare
///     <c>catch</c> is unfiltered; a bare <c>catch when (…)</c> is filtered. Type-parameter catches, error
///     types, and unresolvable types mint nothing (the shared reference-universe gates). A typed
///     <c>catch</c> is recorded <em>beside</em> the type-level edge its type-name syntax mints, never
///     instead of it.
/// </remarks>
/// <remarks>
///     <see cref="SwallowingSites" /> is syntactic in exactly the same way, and its boundary is worth stating
///     because it is the one place a reader could expect analysis: the fact is whether the clause's block's
///     <em>last statement</em> is a <c>throw</c> — bare <c>throw;</c> or <c>throw new X(…)</c>, both being a
///     throw statement — never an all-paths flow analysis. So <c>catch { if (…) return; throw; }</c> counts as
///     throwing and is not a swallowing site, even though one path leaves the handler having suppressed the
///     failure. The axis records what the source spells, and this is the documented honesty boundary.
/// </remarks>
public sealed class CatchEdge
{
    internal CatchEdge(
        TypeNode source,
        TypeNode caught,
        IReadOnlyList<SourceLocation> sites,
        IReadOnlyList<SourceLocation> unfilteredSites,
        IReadOnlyList<SourceLocation> swallowingSites)
    {
        Source = source;
        Caught = caught;
        Sites = sites;
        UnfilteredSites = unfilteredSites;
        SwallowingSites = swallowingSites;
    }

    /// <summary>The catching type.</summary>
    public TypeNode Source { get; }

    /// <summary>The caught exception type.</summary>
    public TypeNode Caught { get; }

    /// <summary>The distinct catch sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     The subset of <see cref="Sites" /> whose <c>catch</c> clause carries no <c>when</c> filter, in the
    ///     same (file, line) order. Empty when every site is filtered.
    /// </summary>
    public IReadOnlyList<SourceLocation> UnfilteredSites { get; }

    /// <summary>
    ///     The subset of <see cref="UnfilteredSites" /> whose <c>catch</c> block does not end in a
    ///     <c>throw</c> statement, in the same (file, line) order — the sites that hold a failure and
    ///     continue. Empty when every unfiltered site rethrows or translates.
    /// </summary>
    public IReadOnlyList<SourceLocation> SwallowingSites { get; }
}

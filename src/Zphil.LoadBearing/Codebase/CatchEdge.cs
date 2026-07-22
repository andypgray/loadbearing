namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed catch edge <c>Source → Caught</c>: <see cref="Source" />'s declaration source has a
///     <c>catch</c> clause whose caught type resolves to <see cref="Caught" /> (GRAMMAR §4.8). A typed
///     <c>catch (IOException e)</c> records <see cref="Caught" /> as <c>IOException</c>; a bare <c>catch</c>
///     records <c>System.Exception</c> — nothing in source names the type, so a bare <c>catch</c> mints no
///     type-level <see cref="ReferenceEdge" /> (the caught channel only). Constructed generics normalize to
///     their open definition (§4.1). <see cref="Sites" /> lists the distinct <c>file:line</c> positions of
///     the <c>catch</c> clauses, deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Caught" /> are the same <see cref="TypeNode" /> instances held by
///     <see cref="CodebaseModel.Types" /> (reference equality, not just name equality). Self-catch (a type
///     catching itself) is never produced — the catch analog of the reference-edge self-drop (§4.1). A
///     rethrowing catch still mints (edges are facts). A <c>when</c> filter never suppresses the edge; its
///     contents mint their own ordinary type/member edges. Type-parameter catches, error types, and
///     unresolvable types mint nothing (the shared reference-universe gates). A typed <c>catch</c> is recorded
///     <em>beside</em> the type-level edge its type-name syntax mints, never instead of it.
/// </remarks>
public sealed class CatchEdge
{
    internal CatchEdge(TypeNode source, TypeNode caught, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Caught = caught;
        Sites = sites;
    }

    /// <summary>The catching type.</summary>
    public TypeNode Source { get; }

    /// <summary>The caught exception type.</summary>
    public TypeNode Caught { get; }

    /// <summary>The distinct catch sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
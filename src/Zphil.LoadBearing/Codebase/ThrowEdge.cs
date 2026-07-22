namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed throw edge <c>Source → Thrown</c>: <see cref="Source" />'s declaration source has a
///     <c>throw</c> statement or throw expression whose thrown expression's static type resolves to
///     <see cref="Thrown" /> (GRAMMAR §4.8). Covers <c>throw new X()</c>, <c>throw ex</c> (the variable's
///     static type), <c>?? throw …</c>, conditional/switch-expression arms, and expression-bodied
///     <c>=&gt; throw new X()</c>. A bare rethrow (<c>throw;</c>), <c>throw null</c>, and type-parameter
///     throws record nothing. Constructed generics normalize to their open definition (§4.1).
///     <see cref="Sites" /> lists the distinct <c>file:line</c> positions of the throws, deduped by
///     (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Thrown" /> are the same <see cref="TypeNode" /> instances held by
///     <see cref="CodebaseModel.Types" /> (reference equality, not just name equality), so an external thrown
///     type is a matchable target like any other external endpoint. Self-throw (a type throwing itself) is
///     never produced — the throw analog of the reference-edge self-drop (§4.1). Throw helpers
///     (<c>ArgumentNullException.ThrowIfNull</c>) are ordinary invocations, not throws, so they mint member-use
///     only and no throw edge. A <c>throw new X()</c> is recorded <em>beside</em> its construction edge and the
///     type-level edge its name syntax mints, never instead of them.
/// </remarks>
public sealed class ThrowEdge
{
    internal ThrowEdge(TypeNode source, TypeNode thrown, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Thrown = thrown;
        Sites = sites;
    }

    /// <summary>The throwing type.</summary>
    public TypeNode Source { get; }

    /// <summary>The thrown exception type.</summary>
    public TypeNode Thrown { get; }

    /// <summary>The distinct throw sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
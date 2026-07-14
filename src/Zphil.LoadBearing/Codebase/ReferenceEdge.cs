namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     A directed reference edge <c>Source → Target</c>: <see cref="Source" />'s declaration source
///     binds a name to <see cref="Target" /> as a type or to any member of it (GRAMMAR §4.1; the
///     operational edge semantics ratified for Phase 2). <see cref="Sites" /> lists the distinct
///     <c>file:line</c> positions where the reference occurs, deduped by (file, line).
/// </summary>
/// <remarks>
///     <see cref="Source" /> and <see cref="Target" /> are the same <see cref="TypeNode" /> instances
///     held by <see cref="CodebaseModel.Types" /> (reference equality, not just name equality).
///     Self-edges (source and target the same type) are never produced.
/// </remarks>
public sealed class ReferenceEdge
{
    internal ReferenceEdge(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        Source = source;
        Target = target;
        Sites = sites;
    }

    /// <summary>The referencing type.</summary>
    public TypeNode Source { get; }

    /// <summary>The referenced type.</summary>
    public TypeNode Target { get; }

    /// <summary>The distinct reference sites, ordered by (file, line).</summary>
    public IReadOnlyList<SourceLocation> Sites { get; }
}
namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One fully-qualified name that denotes two types: a project declares it, and an assembly no project of
///     this solution produces supplies it too — a stand-in declared under a package's own namespace, a
///     polyfill under a BCL one. Both are in <see cref="CodebaseModel.Types" />, and every reference reaches
///     whichever the referencing compilation actually bound.
/// </summary>
/// <remarks>
///     The queryable form of the merge note that reports the same split in prose: a consumer that must act
///     on it — the survey's coverage statement, a rule author asking who the split costs — reads this rather
///     than parsing a sentence, and neither has to rediscover the fact by grouping
///     <see cref="CodebaseModel.Types" /> on name. Sealed class with an internal constructor and get-only
///     props, the <c>Codebase</c> style (records are unavailable on Core's <c>netstandard2.0</c> TFM).
/// </remarks>
public sealed class ShadowedName
{
    internal ShadowedName(
        string fullName, string declaredBy, IReadOnlyList<string> suppliedBy, IReadOnlyList<string> boundFromAssemblyBy)
    {
        FullName = fullName;
        DeclaredBy = declaredBy;
        SuppliedBy = suppliedBy;
        BoundFromAssemblyBy = boundFromAssemblyBy;
    }

    /// <summary>The shared fully-qualified name.</summary>
    public string FullName { get; }

    /// <summary>
    ///     The project that declares it in source — the one whose <see cref="TypeNode" /> is not external.
    ///     Where several declare it, the first (whose facts the node carries), as everywhere else in the model.
    /// </summary>
    public string DeclaredBy { get; }

    /// <summary>The referenced assemblies supplying the same name, ordinal-ordered.</summary>
    public IReadOnlyList<string> SuppliedBy { get; }

    /// <summary>
    ///     The projects whose compilations bound the name from one of <see cref="SuppliedBy" /> rather than
    ///     from the declaration, ordinal-ordered — the half that says whom the split costs.
    /// </summary>
    /// <remarks>
    ///     Read off each project's own binding, not off the reference edges into the assembly's node: a name
    ///     reached solely as a member's parameter or return type mints no edge at all (GRAMMAR §4.6), and a
    ///     bare <c>catch</c> or a <c>throw expr;</c> spells no type name to mint one from — so an
    ///     edge-derived roster would render a project that genuinely binds the assembly as binding nothing.
    ///     Never empty: the entry exists only because some project's compilation created it.
    /// </remarks>
    public IReadOnlyList<string> BoundFromAssemblyBy { get; }
}

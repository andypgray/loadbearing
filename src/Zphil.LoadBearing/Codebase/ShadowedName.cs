namespace Zphil.LoadBearing.Codebase;

/// <summary>
///     One fully-qualified name that means two different types: a project in the solution declares it, and
///     an assembly no project of the solution produces supplies it as well — a stand-in written under a
///     package's own namespace, a polyfill under a framework one. Read them from
///     <see cref="CodebaseModel.ShadowedNames" />.
/// </summary>
/// <remarks>
///     Both types are in <see cref="CodebaseModel.Types" />, and every reference reaches whichever one the
///     referencing compilation actually bound: a project that references the package reaches the package's
///     type and never the declaring project's. A rule naming the type by name reaches both, and a rule
///     naming a project tells them apart, because the supplied type carries the supplying assembly's name in
///     place of a project's. This is the same split <see cref="CodebaseModel.MergeNotes" /> states in prose,
///     in a form a caller can act on without parsing a sentence or grouping
///     <see cref="CodebaseModel.Types" /> by name.
/// </remarks>
// Sealed class, internal constructor, get-only properties: the Codebase style, records being unavailable
// on Core's netstandard2.0 target.
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

    /// <summary>Gets the name both types share.</summary>
    public string FullName { get; }

    /// <summary>
    ///     Gets the project that declares the name in source — the one whose type is not external. Where
    ///     several declare it, the first of them, whose facts that type carries.
    /// </summary>
    public string DeclaredBy { get; }

    /// <summary>Gets the referenced assemblies that supply the same name, ordered ordinal.</summary>
    public IReadOnlyList<string> SuppliedBy { get; }

    /// <summary>
    ///     Gets the projects whose compilations bound the name to one of <see cref="SuppliedBy" /> rather than
    ///     to the declaration, ordered ordinal — the half of the fact that says whom the split costs. Never
    ///     empty: the entry exists because some compilation bound it that way.
    /// </summary>
    // Read off each project's own binding, not off the reference edges into the assembly's node: a name
    // reached solely as a member's parameter or return type mints no edge at all (GRAMMAR §4.6), and a bare
    // catch or a `throw expr;` spells no type name to mint one from — so an edge-derived roster would render
    // a project that genuinely binds the assembly as binding nothing.
    public IReadOnlyList<string> BoundFromAssemblyBy { get; }
}

namespace Zphil.LoadBearing;

/// <summary>
///     The kinds of type a selection can be narrowed to with <c>OfKind</c>, as in
///     <c>arch.Types.OfKind(TypeKind.Interface)</c>, and the kind <c>ITypeInfo.Kind</c> reports. A
///     record class reports <see cref="Class" /> and a record struct <see cref="Struct" />, so
///     <c>ITypeInfo.IsRecord</c> is what tells a record apart.
/// </summary>
// LoadBearing's own enum, never Microsoft.CodeAnalysis.TypeKind: this assembly takes no Roslyn
// dependency.
public enum TypeKind
{
    /// <summary>A class, a record class included.</summary>
    Class,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>A struct, a record struct included.</summary>
    Struct,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>A delegate.</summary>
    Delegate
}

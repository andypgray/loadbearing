namespace Zphil.LoadBearing;

/// <summary>
///     The kinds of type a selection can be narrowed to via <c>.OfKind(...)</c> (GRAMMAR §5.2).
///     This is LoadBearing's own enum — deliberately NOT <c>Microsoft.CodeAnalysis.TypeKind</c>;
///     Roslyn must not enter Core. Records are expressed via the escape hatch in v1.
/// </summary>
public enum TypeKind
{
    /// <summary>A class.</summary>
    Class,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>A struct.</summary>
    Struct,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>A delegate.</summary>
    Delegate
}
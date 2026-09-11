namespace Zphil.LoadBearing;

/// <summary>
///     The accessibility a declaration writes, reported by <c>ITypeInfo.Accessibility</c> and
///     <c>IMemberInfo.Accessibility</c> and tested by <c>MustBePublic</c>, <c>MustBeInternal</c> and
///     <c>MustBePrivate</c>. A top-level type is <see cref="Public" /> or <see cref="Internal" />; the
///     other four values reach only a nested type or a member.
/// </summary>
// LoadBearing's own enum, never Microsoft.CodeAnalysis.Accessibility: this assembly takes no Roslyn
// dependency. Members carry C# keyword names rather than Roslyn's boolean-algebra ones —
// ProtectedOrInternal maps to ProtectedInternal, ProtectedAndInternal to PrivateProtected.
public enum Accessibility
{
    /// <summary><c>public</c>.</summary>
    Public,

    /// <summary><c>internal</c>: reachable from the declaring assembly.</summary>
    Internal,

    /// <summary><c>protected</c>: reachable from a derived type.</summary>
    Protected,

    /// <summary><c>protected internal</c>: reachable from a derived type or from the declaring assembly.</summary>
    ProtectedInternal,

    /// <summary><c>private protected</c>: reachable from a derived type in the declaring assembly.</summary>
    PrivateProtected,

    /// <summary><c>private</c>: a member of a type, or a nested type.</summary>
    Private
}

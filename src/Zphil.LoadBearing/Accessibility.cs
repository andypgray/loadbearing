namespace Zphil.LoadBearing;

/// <summary>
///     The declared accessibility of a type, exposed on <see cref="ITypeInfo" /> and tested by
///     <c>.MustBePublic()</c> / <c>.MustBeInternal()</c> (GRAMMAR §5.3, §5.6). This is
///     LoadBearing's own enum — deliberately NOT <c>Microsoft.CodeAnalysis.Accessibility</c>;
///     Roslyn must not enter Core. Members carry C# keyword names, not Roslyn's boolean-algebra
///     names: <c>ProtectedOrInternal</c> maps to <see cref="ProtectedInternal" />,
///     <c>ProtectedAndInternal</c> to <see cref="PrivateProtected" />.
/// </summary>
public enum Accessibility
{
    /// <summary><c>public</c>.</summary>
    Public,

    /// <summary><c>internal</c>.</summary>
    Internal,

    /// <summary><c>protected</c>.</summary>
    Protected,

    /// <summary><c>protected internal</c> (protected OR internal).</summary>
    ProtectedInternal,

    /// <summary><c>private protected</c> (protected AND internal).</summary>
    PrivateProtected,

    /// <summary><c>private</c> (nested types only).</summary>
    Private
}
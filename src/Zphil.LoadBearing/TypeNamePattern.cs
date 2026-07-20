using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing;

/// <summary>
///     A case-sensitive <c>*</c>-glob over simple type names — the matcher behind
///     <c>WithNameMatching</c> / <c>MustHaveNameMatching</c> (GRAMMAR §5.2, §5.3). Unlike
///     <see cref="NamespacePattern" /> there are no dot segments: a type's simple name is one token,
///     and <c>*</c> matches any run of characters including the empty run. A pattern with no <c>*</c>
///     is an exact (ordinal) match; a lone <c>*</c> matches every name.
/// </summary>
public sealed class TypeNamePattern
{
    private readonly string _pattern;

    /// <summary>Creates a matcher for the given type-name glob.</summary>
    public TypeNamePattern(string pattern)
    {
        _pattern = Guard.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }

    /// <summary>Whether the given simple type name matches the pattern.</summary>
    public bool Matches(string name)
    {
        Guard.NotNull(name, nameof(name));
        return Wildcard.Match(_pattern, name);
    }
}
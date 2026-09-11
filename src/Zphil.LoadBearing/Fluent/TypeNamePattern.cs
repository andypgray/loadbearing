using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing.Fluent;

/// <summary>
///     Matches simple type names against a glob, the way <c>WithNameMatching</c> and
///     <c>MustHaveNameMatching</c> match. A simple name is one token with no dot structure, so the
///     rules are short: <c>*</c> matches any run of characters, the empty run included (<c>*Async</c>
///     matches <c>Async</c>), matching is case-sensitive, a glob with no <c>*</c> matches that one name
///     exactly, and a lone <c>*</c> matches every name. The name it tests is the one a report prints
///     without namespace, generic arity or containing type: <c>Repository&lt;T&gt;</c> is
///     <c>Repository</c>, and a nested <c>Order.Line</c> is <c>Line</c>.
/// </summary>
public sealed class TypeNamePattern
{
    private readonly string _pattern;

    /// <summary>
    ///     Creates a matcher for a type-name glob, such as <c>new TypeNamePattern("*Controller")</c>.
    ///     Throws <see cref="ArgumentNullException" /> when the glob is null and
    ///     <see cref="ArgumentException" /> when it is blank.
    /// </summary>
    public TypeNamePattern(string pattern)
    {
        _pattern = Guard.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }

    /// <summary>
    ///     Whether a simple type name matches the glob — the name without its namespace, generic arity or
    ///     containing type. Throws <see cref="ArgumentNullException" /> when it is null.
    /// </summary>
    public bool Matches(string name)
    {
        Guard.NotNull(name, nameof(name));
        return Wildcard.Match(_pattern, name);
    }
}

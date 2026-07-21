using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Model;

namespace Zphil.LoadBearing;

/// <summary>
///     The v1 member-adjective vocabulary (GRAMMAR §5.7) as <b>generic self-type</b> extension methods
///     on <see cref="MemberSelection" />. The <c>TSelf</c> shape preserves the concrete member-selection
///     type, so refining a <see cref="MethodSelection" /> returns a <see cref="MethodSelection" /> and
///     <c>.Returning</c> stays reachable after any adjective. Each call appends one closed-vocabulary
///     adjective and returns a fresh selection (member selections are immutable values). These live on
///     a hierarchy disjoint from <see cref="Selection" />, so the identically-named type-side adjectives
///     never collide on overload resolution — the receiver type decides.
/// </summary>
public static class MemberSelectionAdjectives
{
    /// <summary>Narrows to members whose name ends with a suffix: " named `*Async`".</summary>
    public static TSelf WithSuffix<TSelf>(this TSelf selection, string suffix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>Narrows to members whose name starts with a prefix: " named `Get*`".</summary>
    public static TSelf WithPrefix<TSelf>(this TSelf selection, string prefix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>Narrows to members whose name matches a glob: " whose name matches `*Handler*`".</summary>
    public static TSelf WithNameMatching<TSelf>(this TSelf selection, string glob)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     The member selector-position escape hatch. The predicate is stored, never evaluated at
    ///     spec build; the required <paramref name="description" /> renders as a sentence-final relative
    ///     clause (GRAMMAR §5.6, §5.7). A blank description fails spec build (validation §8 item 5).
    /// </summary>
    public static TSelf Where<TSelf>(this TSelf selection, Func<IMemberInfo, bool> predicate, string description)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWhereAdjective(NotNull(predicate, nameof(predicate)), description));
    }

    private static TSelf Append<TSelf>(TSelf selection, MemberAdjective adjective)
        where TSelf : MemberSelection
    {
        Guard.NotNull(selection, nameof(selection));
        return (TSelf)selection.Refined(adjective);
    }

    private static T NotNull<T>(T value, string paramName)
        where T : class
    {
        return Guard.NotNull(value, paramName);
    }
}
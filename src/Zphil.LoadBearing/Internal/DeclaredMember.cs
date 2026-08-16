using System.Reflection;

namespace Zphil.LoadBearing.Internal;

/// <summary>
///     The single "what counts as a declared member" policy, shared by validation and prose. A member
///     anchor is definition-level (GRAMMAR §4.5), so the lookup normalizes a constructed generic to its
///     definition and then asks for members declared on that type alone — public and non-public,
///     instance and static.
/// </summary>
/// <remarks>
///     Validation decides whether an anchor's member exists at all
///     (<see cref="Validation.SpecValidator" />) and prose decides whether it renders a trailing
///     <c>()</c> (<see cref="Member.IsMethod" />); the two must agree, or a spec that validates clean
///     renders a member the check never found. One lookup is what makes them agree.
/// </remarks>
internal static class DeclaredMember
{
    /// <summary>The binding flags that define declared membership: declared-only, both visibilities, both scopes.</summary>
    internal const BindingFlags Flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                                        BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    ///     The members <paramref name="type" />'s generic definition declares under
    ///     <paramref name="name" /> — empty when the name is blank, typo'd, or inherited rather than declared.
    /// </summary>
    internal static MemberInfo[] Of(Type type, string name)
    {
        Type definition = Generics.Definition(type);
        return definition.GetMember(name, Flags);
    }
}

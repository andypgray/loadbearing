using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Cli.Rendering;

/// <summary>
///     How the CLI spells a member: its declaring type, a dot, its name, and <c>()</c> iff it is a method.
/// </summary>
/// <remarks>
///     Two overloads because a member reaches a surface as two shapes — a <see cref="MemberReference" /> for
///     the banned member a rule names, a <see cref="MemberNode" /> for the offending member a shape rule
///     judges — differing only in how the declaring type is reached. The spelling itself is one primitive
///     rather than a per-surface choice, because it is not only rendered: <c>baseline --add</c> resolves
///     names against it and lists candidates in it, so a form that drifted from what <c>check</c> prints
///     would ask an operator to retype a name the product never showed them.
/// </remarks>
internal static class MemberDisplay
{
    /// <summary>The banned member a rule names, as declaring-type-dot-member.</summary>
    /// <param name="member">The member reference an edge violation carries.</param>
    internal static string Of(MemberReference member)
    {
        return $"{member.ContainingType.FullName}.{member.Name}{Suffix(member.Kind)}";
    }

    /// <summary>The offending member a shape rule judges, as declaring-type-dot-member.</summary>
    /// <param name="member">The member node a member-shape violation carries.</param>
    internal static string Of(MemberNode member)
    {
        return $"{member.DeclaringTypeFullName}.{member.Name}{Suffix(member.Kind)}";
    }

    private static string Suffix(MemberKind kind)
    {
        return kind == MemberKind.Method ? "()" : string.Empty;
    }
}

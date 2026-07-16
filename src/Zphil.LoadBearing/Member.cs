using System.Reflection;
using Zphil.LoadBearing.Internal;

namespace Zphil.LoadBearing;

/// <summary>
///     A member-access ban target (GRAMMAR §4.5) — a declaring type plus a member name, minted by
///     <see cref="Arch.Member" /> for <see cref="SelectionConstraints.MustNotUse" />. A target-only
///     leaf, deliberately <em>not</em> a <see cref="Selection" />: it never enters the selection
///     hierarchy, so adjectives and modal verbs are uncompilable on it by construction (GRAMMAR §3.2).
///     Matching is by declaring type + member name, so one ban covers every overload — there is no
///     signature form. Owner-stamped like a selection and reusable across rules on the same
///     <see cref="Arch" /> (the fresh-instance contract covers it, GRAMMAR §8 item 13).
/// </summary>
public sealed class Member
{
    private const BindingFlags MemberFlags =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    internal Member(Arch owner, Type declaringType, string name)
    {
        Owner = owner;
        DeclaringType = Guard.NotNull(declaringType, nameof(declaringType));
        Name = Guard.NotNull(name, nameof(name));
        IsMethod = ResolveIsMethod(DeclaringType, Name);
    }

    /// <summary>
    ///     The <see cref="Arch" /> this member was minted on (GRAMMAR §3.2 fresh-instance contract,
    ///     member flavor).
    /// </summary>
    internal Arch Owner { get; }

    /// <summary>The declaring type exactly as authored — the <c>typeof(...)</c> operand.</summary>
    internal Type DeclaringType { get; }

    /// <summary>The member name — the <c>nameof(...)</c> operand.</summary>
    internal string Name { get; }

    /// <summary>
    ///     Whether the anchored member resolves to a method, decided once at construction against the
    ///     normalized generic definition; drives the trailing <c>()</c> in prose (GRAMMAR §6).
    /// </summary>
    internal bool IsMethod { get; }

    // Normalize a closed/constructed generic anchor to its definition (Task<int> → Task<>), then look
    // for a declared member of that name: any hit that is a MethodInfo makes this a method. A blank or
    // typo'd name yields no hits (false); validation rejects those later (GRAMMAR §8 items 11–12).
    private static bool ResolveIsMethod(Type declaringType, string name)
    {
        Type anchor = declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition
            ? declaringType.GetGenericTypeDefinition()
            : declaringType;

        foreach (MemberInfo hit in anchor.GetMember(name, MemberFlags))
            if (hit is MethodInfo)
                return true;

        return false;
    }
}
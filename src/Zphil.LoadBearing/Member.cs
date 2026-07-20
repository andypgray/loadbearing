using System.Reflection;
using Zphil.LoadBearing.Internal;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing;

/// <summary>
///     A member-access ban target (GRAMMAR §4.5) — a declaring type plus a member name, minted by
///     <see cref="Arch.Member(System.Type,System.String)" /> (or the expression-anchor overloads
///     <c>arch.Member&lt;T&gt;(x =&gt; x.M)</c> / <c>arch.Member(() =&gt; Type.M)</c>, which desugar at
///     mint to the same leaf) for <see cref="SelectionConstraints.MustNotUse" />. A target-only
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

    private readonly Type? _declaringType;
    private readonly bool _isMethod;
    private readonly string? _name;

    internal Member(Arch owner, Type declaringType, string name, SpecSourceLocation? location = null)
    {
        Owner = owner;
        Location = location;
        _declaringType = Guard.NotNull(declaringType, nameof(declaringType));
        _name = Guard.NotNull(name, nameof(name));
        _isMethod = ResolveIsMethod(_declaringType, _name);
    }

    // Poison ctor for an unresolvable member-anchor expression (GRAMMAR §8, MemberExpressionUnresolvable):
    // MemberExpressionResolver could not reduce the lambda to a declared (type, name). It records only the
    // diagnostic core and leaves the DeclaringType/Name/IsMethod backing fields at their defaults — reading
    // any of the three now throws (fail closed, enforced not merely documented), so a poisoned Member can
    // never be mistaken for a resolved one. Nothing reads them anyway: SpecValidator.CheckMember
    // short-circuits on PoisonError, and ArchModelBuilder.Build validates the whole spec before it projects
    // or renders any node, so a poisoned Member is caught before anything reaches for its anchor. Owner is
    // stamped so the foreign-Arch check (§8 item 13) still precedes the poison report.
    internal Member(Arch owner, string poisonError, SpecSourceLocation? location = null)
    {
        Owner = owner;
        PoisonError = poisonError;
        Location = location;
    }

    /// <summary>
    ///     The <see cref="Arch" /> this member was minted on (GRAMMAR §3.2 fresh-instance contract,
    ///     member flavor).
    /// </summary>
    internal Arch Owner { get; }

    /// <summary>
    ///     The spec-source position of the <c>arch.Member(...)</c> call that minted this leaf (GRAMMAR §8),
    ///     or null for a verb-minted member (the <c>MustNotUse(() =&gt; ...)</c> expression forms cannot carry
    ///     caller info past <c>params</c>) — whose errors then attribute to the consuming rule's anchor.
    ///     Always safe to read, including on a poisoned member (unlike the fail-closed anchor accessors).
    /// </summary>
    internal SpecSourceLocation? Location { get; }

    /// <summary>
    ///     Non-null when this member was minted from an unresolvable anchor expression: the diagnostic
    ///     core (GRAMMAR §8, <see cref="Validation.SpecValidationErrorCode.MemberExpressionUnresolvable" />)
    ///     that <see cref="Validation.SpecValidator" /> reports before any reader touches the fail-closed
    ///     <see cref="DeclaringType" />/<see cref="Name" />. Null for every resolved member.
    /// </summary>
    internal string? PoisonError { get; }

    /// <summary>The declaring type exactly as authored — the <c>typeof(...)</c> operand.</summary>
    internal Type DeclaringType => PoisonError is null ? _declaringType! : throw PoisonRead();

    /// <summary>The member name — the <c>nameof(...)</c> operand.</summary>
    internal string Name => PoisonError is null ? _name! : throw PoisonRead();

    /// <summary>
    ///     Whether the anchored member resolves to a method, decided once at construction against the
    ///     normalized generic definition; drives the trailing <c>()</c> in prose (GRAMMAR §6).
    /// </summary>
    internal bool IsMethod => PoisonError is null ? _isMethod : throw PoisonRead();

    // A poisoned member anchor never populated its resolved fields; reading one is a caller bug (every real
    // reader runs after SpecValidator.CheckMember has reported the PoisonError). Fail closed rather than hand
    // back a default that could masquerade as a resolved anchor.
    private InvalidOperationException PoisonRead()
    {
        return new InvalidOperationException(
            "A poisoned member anchor's DeclaringType/Name/IsMethod must not be read; " +
            "the anchor expression was unresolvable and its PoisonError is reported at spec build first.");
    }

    // Normalize a closed/constructed generic anchor to its definition (Task<int> → Task<>), then look
    // for a declared member of that name: any hit that is a MethodInfo makes this a method. A blank or
    // typo'd name yields no hits (false); validation rejects those later (GRAMMAR §8 items 11–12).
    private static bool ResolveIsMethod(Type declaringType, string name)
    {
        Type anchor = Generics.Definition(declaringType);

        foreach (MemberInfo hit in anchor.GetMember(name, MemberFlags))
            if (hit is MethodInfo)
                return true;

        return false;
    }
}
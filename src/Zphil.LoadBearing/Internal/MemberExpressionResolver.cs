using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Zphil.LoadBearing.Validation;

namespace Zphil.LoadBearing.Internal;

/// <summary>
///     Reduces a member-anchor lambda — <c>arch.Member&lt;T&gt;(x =&gt; x.M)</c> or
///     <c>arch.Member(() =&gt; Type.M)</c> (GRAMMAR §4.5) — to the same <see cref="Member" /> leaf the
///     <c>typeof</c> + <c>nameof</c> form mints, or to a poisoned <see cref="Member" /> whose
///     <see cref="Member.PoisonError" /> the §8 validation pass reports (
///     <see cref="Validation.SpecValidationErrorCode.MemberExpressionUnresolvable" />)
///     before anything reads the anchor. Pure authoring sugar: the anchor is the tree's <em>resolved</em>
///     member — <see cref="MemberInfo.DeclaringType" /> (never <see cref="MemberInfo.ReflectedType" />),
///     with a constructed generic normalized to its definition — so an expression-minted member reifies,
///     renders, and checks byte-identically to the <c>typeof</c>-minted one. First repo use of
///     <see cref="System.Linq.Expressions" />.
/// </summary>
internal static class MemberExpressionResolver
{
    // Seven poison messages, one code (the BlankPattern precedent). The resolver stores the core; the
    // validator appends " (used by '{id}')." Each names the shape that failed and steers to the cure.
    private const string NotMemberAccess =
        "A member anchor lambda must be a single property, field, or method access " +
        "(x => x.Member or () => Type.Member); this lambda body is neither";

    private const string MethodGroup =
        "A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); write the " +
        "invocation form (x => x.Method() or () => Type.Method(...)) so the method itself is anchored";

    private const string ReceiverNotParameter =
        "A member anchor lambda must reach its member directly on the lambda parameter (an interface cast " +
        "or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
        "user-defined conversion is not); anchor the declaring type you mean directly";

    private const string StaticInInstanceForm =
        "A typed member anchor arch.Member<T>(x => ...) accesses a static member; anchor statics with the " +
        "parameterless overload arch.Member(() => Type.Member)";

    private const string InstanceInStaticForm =
        "A parameterless member anchor arch.Member(() => ...) must access a static member directly; anchor " +
        "an instance member with the typed overload arch.Member<T>(x => x.Member)";

    private const string SpecialName =
        "A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
        "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method";

    private const string CompileTimeConstant =
        "A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
        "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
        "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M))";

    /// <summary>
    ///     Resolves <paramref name="lambda" /> (already null-checked by the calling <see cref="Arch" />
    ///     <c>Member</c> overload) to a resolved or poisoned <see cref="Member" /> stamped with
    ///     <paramref name="owner" /> and the anchor's spec-source <paramref name="location" /> (null for a
    ///     verb-minted lambda, which cannot carry caller info — GRAMMAR §8).
    /// </summary>
    internal static Member Resolve(Arch owner, LambdaExpression lambda, SpecSourceLocation? location = null)
    {
        bool instanceForm = lambda.Parameters.Count == 1;
        Expression body = Unwrap(lambda.Body);

        switch (body)
        {
            case MethodCallExpression call:
                // Order matters (ratified): the method-group trap is detected before receiver
                // classification (a lowered method group has a synthetic receiver), then indexers.
                if (IsMethodGroupLowering(call)) return Poison(owner, MethodGroup, location);
                if (call.Method.IsSpecialName) return Poison(owner, SpecialName, location);

                // In the instance form a reduced extension call `x.Ext()` lowers to `Ext(x)` — a static call
                // whose real receiver is Arguments[0], redirected so the parameter check sees it (the §4.5
                // ReducedFrom parity). In the static form an extension is anchored like any other static call
                // (its declaring static class), so no redirect.
                Expression? methodReceiver = instanceForm && IsExtensionCall(call) ? call.Arguments[0] : call.Object;
                return ClassifyReceiver(owner, methodReceiver, instanceForm, lambda, location)
                       ?? Anchor(owner, call.Method.DeclaringType!, call.Method.Name, location);

            case MemberExpression { Member: PropertyInfo or FieldInfo } member:
                return ClassifyReceiver(owner, member.Expression, instanceForm, lambda, location)
                       ?? Anchor(owner, member.Member.DeclaringType!, member.Member.Name, location);

            case ConstantExpression:
                // The lambda body inlined to a compile-time value (a const field, enum member, or literal),
                // peeled by Unwrap to its ConstantExpression — no member remains in the tree to anchor.
                return Poison(owner, CompileTimeConstant, location);

            default:
                return Poison(owner, NotMemberAccess, location);
        }
    }

    // The receiver rules (ratified): the instance form wants the lambda parameter itself, reached only
    // through identity-preserving casts — a reference/interface Convert with no operator method, or an
    // as-cast (both peeled by PeelIdentityCast); a user-defined conversion stops the peel and is reported,
    // since following it would silently anchor the post-conversion type. The static form wants no receiver.
    // Returns a poison Member on a mismatch, or null to mean "receiver is fine".
    private static Member? ClassifyReceiver(
        Arch owner, Expression? receiver, bool instanceForm, LambdaExpression lambda, SpecSourceLocation? location)
    {
        Expression? peeled = PeelIdentityCast(receiver);

        if (instanceForm)
        {
            if (peeled is null) return Poison(owner, StaticInInstanceForm, location);
            if (!ReferenceEquals(peeled, lambda.Parameters[0])) return Poison(owner, ReceiverNotParameter, location);
            return null;
        }

        return peeled is null ? null : Poison(owner, InstanceInStaticForm, location);
    }

    // The anchor is the resolved member's declaring type (never ReflectedType), normalized through
    // Generics.Definition so a constructed generic collapses to its definition (Task<int> → Task<>). That
    // keeps it identical to the typeof form and the checker's closed-generic refusal (GRAMMAR §4.5)
    // unreachable from an expression-minted member.
    private static Member Anchor(Arch owner, Type declaringType, string name, SpecSourceLocation? location)
    {
        return new Member(owner, Generics.Definition(declaringType), name, location);
    }

    // The body's boxing Convert (a value member widened to object? by the Func<...,object?> overloads) is
    // peeled before classification — distinct from a receiver-side Convert (an interface cast), which
    // ClassifyReceiver peels separately so it can still recognise the parameter underneath.
    private static Expression Unwrap(Expression body)
    {
        return PeelConvert(body)!;
    }

    private static Expression? PeelConvert(Expression? expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            expression = convert.Operand;
        return expression;
    }

    // The receiver-side peel: only identity-preserving casts are transparent — a reference/interface Convert
    // with no operator method, or an as-cast. Both leave the underlying parameter reachable while the tree's
    // resolved member stays on the cast-to type (the §4.5 dispatch boundary). A user-defined conversion (a
    // Convert carrying an operator Method) is deliberately NOT peeled, so ClassifyReceiver reports it rather
    // than silently anchoring the post-conversion type.
    private static Expression? PeelIdentityCast(Expression? expression)
    {
        while (expression is UnaryExpression convert
               && (convert.NodeType is ExpressionType.TypeAs
                   || (convert.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked && convert.Method is null)))
            expression = convert.Operand;
        return expression;
    }

    // C# 14 lowers a method-group body `x => x.M` or `() => T.M` to a CreateDelegate materialization that
    // carries the target MethodInfo as a compiler-emitted constant (C# ≤13 refuses a method group in a tree
    // — all consumers are net10/C# 14): either the receiver is that constant MethodInfo (instance
    // MethodInfo.CreateDelegate), or a CreateDelegate on MethodInfo/Delegate takes it as a constant argument.
    // A genuine CreateDelegate invocation instead passes a captured local (a closure-field access) or a
    // typeof — never a bare MethodInfo constant — so it anchors normally rather than tripping the trap.
    private static bool IsMethodGroupLowering(MethodCallExpression call)
    {
        if (call.Object is ConstantExpression { Value: MethodInfo }) return true;

        return call.Method.Name == nameof(MethodInfo.CreateDelegate)
               && (call.Method.DeclaringType == typeof(MethodInfo) || call.Method.DeclaringType == typeof(Delegate))
               && call.Arguments.Any(argument => argument is ConstantExpression { Value: MethodInfo });
    }

    private static bool IsExtensionCall(MethodCallExpression call)
    {
        return call.Object is null
               && call.Arguments.Count > 0
               && call.Method.IsDefined(typeof(ExtensionAttribute), false);
    }

    private static Member Poison(Arch owner, string message, SpecSourceLocation? location)
    {
        return new Member(owner, message, location);
    }
}
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing;

/// <summary>
///     The read-only view of a declared member handed to member escape-hatch predicates
///     (<c>.Where(pred, ...)</c> and <c>.Must(pred, ...)</c> on a member selection, GRAMMAR §4.6,
///     §5.6). The member analog of <see cref="ITypeInfo" />: the v1 member-predicate input contract,
///     grown additively as extraction learns new facts. Predicates are stored on the model but never
///     evaluated in this phase — the mandatory description is what renders, not the lambda.
/// </summary>
public interface IMemberInfo
{
    /// <summary>The member's simple name (no declaring-type qualifier, no signature).</summary>
    string Name { get; }

    /// <summary>The kind of member (method, property, field, or event).</summary>
    MemberKind Kind { get; }

    /// <summary>
    ///     The type that declares the member, as the reused type-side contract — so a member
    ///     predicate can reach its declaring type's facts (GRAMMAR §4.6).
    /// </summary>
    ITypeInfo DeclaringType { get; }

    /// <summary>The declared accessibility.</summary>
    Accessibility Accessibility { get; }

    /// <summary>Whether the member is declared <c>static</c>.</summary>
    bool IsStatic { get; }

    /// <summary>
    ///     Whether the member is abstract, in C# declaration semantics: an <c>abstract</c> member and
    ///     every interface member report <c>true</c> (GRAMMAR §4.6).
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    ///     Whether the member is virtual, in C# declaration semantics: a <c>virtual</c> member reports
    ///     <c>true</c>, but an <c>override</c> or <c>abstract</c> member reports <c>false</c> — an
    ///     override is not itself "virtual" in the authored sense (GRAMMAR §4.6).
    /// </summary>
    bool IsVirtual { get; }

    /// <summary>Whether the member is a method declared with the <c>async</c> keyword.</summary>
    bool IsAsync { get; }

    /// <summary>
    ///     The definition-level full name of a method's return type (<c>System.Void</c> for a void
    ///     method), or null for a non-method member. This is what a <c>.Returning</c> anchor matches
    ///     against (GRAMMAR §4.6).
    /// </summary>
    string? ReturnTypeFullName { get; }

    /// <summary>
    ///     The definition-level full name of a property/field/event member type, or null for a method.
    /// </summary>
    string? MemberTypeFullName { get; }

    /// <summary>The distinct file paths declaring the member, verbatim as compiled.</summary>
    IReadOnlyList<string> FilePaths { get; }
}
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Roslyn.Caching;

/// <summary>
///     The scalar facts of one declared member (GRAMMAR §4.6), extracted from a Roslyn symbol but holding
///     no Roslyn types — the member analog of <see cref="TypeFacts" /> and the pure-data payload the merge
///     turns into a <see cref="Zphil.LoadBearing.Codebase.MemberNode" />. Read once per member from the
///     declaring type's <c>OriginalDefinition</c> so a persisted fragment rebuilds the
///     inventory without re-binding.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Kind" /> and <see cref="Accessibility" /> are the Core enums (never Roslyn's), so
///         this record — like the rest of the fragment DTO — carries no dependency on
///         <c>Microsoft.CodeAnalysis</c> and round-trips through System.Text.Json unchanged.
///     </para>
///     <para>
///         The flags carry C# declaration semantics, not IL: <see cref="IsVirtual" /> is true for a
///         <c>virtual</c> member and false for an <c>override</c> or <c>abstract</c> one;
///         <see cref="IsAbstract" /> is true for an <c>abstract</c> member and for every interface member;
///         <see cref="IsAsync" /> reflects the <c>async</c> keyword. Exactly one of
///         <see cref="ReturnTypeFullName" /> (methods; <c>System.Void</c> for a void method) and
///         <see cref="MemberTypeFullName" /> (properties/fields/events) is non-null; both are the
///         definition-level FQN in extraction format, so a method's <see cref="ReturnTypeFullName" />
///         compares equal to the <c>.Returning</c> anchor's <c>TypeName.FullDisplay</c> (GRAMMAR §4.6).
///     </para>
///     <para>
///         <see cref="Parameters" /> is the method's declared parameters in declaration order — each a
///         <see cref="ParameterFacts" /> whose type is normalized by the same helper as
///         <see cref="ReturnTypeFullName" /> (GRAMMAR §4.6, §5.6). It is empty for properties, fields, and
///         events, and for a parameterless method.
///     </para>
/// </remarks>
internal sealed record MemberFacts(
    string SymbolId,
    string Name,
    MemberKind Kind,
    Accessibility Accessibility,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsAsync,
    string? ReturnTypeFullName,
    string? MemberTypeFullName,
    IReadOnlyList<ParameterFacts> Parameters);
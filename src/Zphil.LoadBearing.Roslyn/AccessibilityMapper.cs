using Microsoft.CodeAnalysis;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Maps Roslyn's <see cref="RoslynAccessibility" /> onto LoadBearing's Core
///     <see cref="CoreAccessibility" /> (the two enums are deliberately distinct — Roslyn must
///     not enter Core; the alias pattern matches <see cref="TypeKindMapper" />). Core uses C#
///     keyword names: ProtectedOrInternal → ProtectedInternal, ProtectedAndInternal →
///     PrivateProtected. <c>NotApplicable</c> is the one value with no C# declaration meaning, so the
///     two entry points differ in what they do with it: <see cref="TryMap" /> reports failure and lets
///     the caller choose a fallback, while <see cref="Map(ISymbol)" /> throws.
/// </summary>
/// <remarks>
///     <para>
///         <b>Where the hard invariant actually holds.</b> GRAMMAR §4.6 scopes "always carries a real
///         declared accessibility" to the <em>declared members</em> of solution-declared types — an
///         Ordinary method, a non-indexer property, a field, or an event. That path keeps
///         <see cref="Map(ISymbol)" />: a member with no keyword meaning is a broken inventory, not
///         input to tolerate.
///     </para>
///     <para>
///         <b>Where it does not.</b> A type fact can be minted for a symbol the compiler never
///         resolved — a partially-loaded workspace yields error symbols, whose
///         <see cref="ISymbol.DeclaredAccessibility" /> is <c>NotApplicable</c>, and the external-mint
///         path reaches them through base types, interfaces, and attribute classes. Those are external
///         types carrying only a shallow hierarchy (§5.2), never rule subjects (§4.1), so the type path
///         uses <see cref="TryMap" /> and falls back rather than crashing a survey that has no spec to
///         answer to.
///     </para>
/// </remarks>
internal static class AccessibilityMapper
{
    public static CoreAccessibility Map(INamedTypeSymbol symbol)
    {
        return Map((ISymbol)symbol);
    }

    /// <summary>
    ///     The member overload (GRAMMAR §4.6): the same six-way declared-accessibility mapping over any
    ///     inventoried member symbol — private included (a member-only accessibility, §5.7). Throws on
    ///     <c>NotApplicable</c>; callers that can legitimately see an unresolved symbol call
    ///     <see cref="TryMap" /> instead.
    /// </summary>
    public static CoreAccessibility Map(ISymbol symbol)
    {
        if (TryMap(symbol, out CoreAccessibility accessibility)) return accessibility;

        throw new InvalidOperationException(
            $"Symbol '{symbol.ToDisplayString()}' reports accessibility '{symbol.DeclaredAccessibility}', which has no C# declaration meaning here.");
    }

    /// <summary>
    ///     The total twin of <see cref="Map(ISymbol)" /> (the <see cref="TypeKindMapper.TryMap" /> pattern):
    ///     the same six-way mapping, returning <see langword="false" /> for <c>NotApplicable</c> instead of
    ///     throwing, so a caller holding an unresolved symbol can choose a fallback.
    /// </summary>
    public static bool TryMap(ISymbol symbol, out CoreAccessibility accessibility)
    {
        switch (symbol.DeclaredAccessibility)
        {
            case RoslynAccessibility.Public:
                accessibility = CoreAccessibility.Public;
                return true;
            case RoslynAccessibility.Internal:
                accessibility = CoreAccessibility.Internal;
                return true;
            case RoslynAccessibility.Protected:
                accessibility = CoreAccessibility.Protected;
                return true;
            case RoslynAccessibility.ProtectedOrInternal:
                accessibility = CoreAccessibility.ProtectedInternal;
                return true;
            case RoslynAccessibility.ProtectedAndInternal:
                accessibility = CoreAccessibility.PrivateProtected;
                return true;
            case RoslynAccessibility.Private:
                accessibility = CoreAccessibility.Private;
                return true;
            default:
                accessibility = default;
                return false;
        }
    }
}
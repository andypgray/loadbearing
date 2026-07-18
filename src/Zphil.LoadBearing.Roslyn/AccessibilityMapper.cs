using Microsoft.CodeAnalysis;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Maps Roslyn's <see cref="RoslynAccessibility" /> onto LoadBearing's Core
///     <see cref="CoreAccessibility" /> (the two enums are deliberately distinct — Roslyn must
///     not enter Core; the alias pattern matches <see cref="TypeKindMapper" />). Core uses C#
///     keyword names: ProtectedOrInternal → ProtectedInternal, ProtectedAndInternal →
///     PrivateProtected. <c>NotApplicable</c> is unreachable for a named type or an inventoried
///     member (GRAMMAR §4.6 — an Ordinary method, a non-indexer property, a field, or an event
///     always carries a real declared accessibility), so it throws rather than guessing.
/// </summary>
internal static class AccessibilityMapper
{
    public static CoreAccessibility Map(INamedTypeSymbol symbol)
    {
        return Map((ISymbol)symbol);
    }

    /// <summary>
    ///     The member overload (GRAMMAR §4.6): the same six-way declared-accessibility mapping over any
    ///     inventoried member symbol — private included (a member-only accessibility, §5.7).
    /// </summary>
    public static CoreAccessibility Map(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility switch
        {
            RoslynAccessibility.Public => CoreAccessibility.Public,
            RoslynAccessibility.Internal => CoreAccessibility.Internal,
            RoslynAccessibility.Protected => CoreAccessibility.Protected,
            RoslynAccessibility.ProtectedOrInternal => CoreAccessibility.ProtectedInternal,
            RoslynAccessibility.ProtectedAndInternal => CoreAccessibility.PrivateProtected,
            RoslynAccessibility.Private => CoreAccessibility.Private,
            _ => throw new InvalidOperationException(
                $"Symbol '{symbol.ToDisplayString()}' reports accessibility '{symbol.DeclaredAccessibility}', which has no C# declaration meaning here.")
        };
    }
}
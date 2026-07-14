using Microsoft.CodeAnalysis;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Maps Roslyn's <see cref="RoslynAccessibility" /> onto LoadBearing's Core
///     <see cref="CoreAccessibility" /> (the two enums are deliberately distinct — Roslyn must
///     not enter Core; the alias pattern matches <see cref="TypeKindMapper" />). Core uses C#
///     keyword names: ProtectedOrInternal → ProtectedInternal, ProtectedAndInternal →
///     PrivateProtected. <c>NotApplicable</c> is unreachable for named types, so it throws
///     rather than guessing.
/// </summary>
internal static class AccessibilityMapper
{
    public static CoreAccessibility Map(INamedTypeSymbol symbol)
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
                $"Named type '{symbol.ToDisplayString()}' reports accessibility '{symbol.DeclaredAccessibility}', which has no C# declaration meaning for a named type.")
        };
    }
}
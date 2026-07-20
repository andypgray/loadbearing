using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="TypeKindMapper.TryMap" /> maps the five representable Roslyn type kinds and reports every
///     other kind as unmappable via <see langword="false" /> (its caller then skips the symbol). An error type
///     symbol reports <c>TypeKind.Error</c> — none of the five — so it drives the fail-closed default arm.
/// </summary>
public sealed class TypeKindMapperTests
{
    [Fact]
    public void TryMap_UnrepresentableTypeKind_ReturnsFalse()
    {
        // An error type symbol (TypeKind.Error) is not Class/Interface/Struct/Enum/Delegate, so TryMap fails
        // closed. No metadata references are needed to mint one.
        var compilation = CSharpCompilation.Create("t");
        INamedTypeSymbol errorSymbol = compilation.CreateErrorTypeSymbol(compilation.GlobalNamespace, "Ghost", 0);

        TypeKindMapper.TryMap(errorSymbol, out _).ShouldBeFalse();
    }
}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="AccessibilityMapper" /> maps Roslyn's six declared accessibilities onto Core's C#-keyword
///     names. A symbol whose <c>DeclaredAccessibility</c> is <c>NotApplicable</c> has no C# declaration
///     meaning, and the two entry points differ in what they do about it: <c>Map</c> throws (the fail-closed
///     default arm the member inventory relies on), <c>TryMap</c> reports failure so a caller holding a
///     symbol the compiler never resolved can choose a fallback instead of crashing.
/// </summary>
public sealed class AccessibilityMapperTests
{
    [Fact]
    public void Map_SymbolWithNotApplicableAccessibility_Throws()
    {
        // An array type symbol reports Accessibility.NotApplicable — the one value with no keyword meaning —
        // reaching the default arm via the ISymbol overload (an IArrayTypeSymbol is not an INamedTypeSymbol).
        IArrayTypeSymbol arrayType = ArrayTypeSymbol();

        Should.Throw<InvalidOperationException>(() => AccessibilityMapper.Map(arrayType));
    }

    [Fact]
    public void TryMap_SymbolWithNotApplicableAccessibility_ReturnsFalseWithoutThrowing()
    {
        // The same symbol through the total twin: no throw, and the caller is told the mapping failed.
        IArrayTypeSymbol arrayType = ArrayTypeSymbol();

        bool mapped = AccessibilityMapper.TryMap(arrayType, out CoreAccessibility accessibility);

        mapped.ShouldBeFalse();
        accessibility.ShouldBe(default);
    }

    [Fact]
    public void TryMap_UnresolvedType_ReturnsFalse()
    {
        // The case the fallback exists for, and the negative control for the extraction-tolerance tests: a
        // type the compiler could not resolve — what a partially-loaded workspace hands the external-mint
        // path — is an error symbol carrying no declared accessibility. Without a total mapper, a missing
        // project reference surfaced as a crash naming a symbol nobody wrote.
        INamedTypeSymbol unresolved = UnresolvedBaseTypeSymbol();

        unresolved.TypeKind.ShouldBe(RoslynTypeKind.Error);
        unresolved.DeclaredAccessibility.ShouldBe(RoslynAccessibility.NotApplicable);
        AccessibilityMapper.TryMap(unresolved, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("public", CoreAccessibility.Public)]
    [InlineData("internal", CoreAccessibility.Internal)]
    [InlineData("protected", CoreAccessibility.Protected)]
    [InlineData("protected internal", CoreAccessibility.ProtectedInternal)]
    [InlineData("private protected", CoreAccessibility.PrivateProtected)]
    [InlineData("private", CoreAccessibility.Private)]
    public void TryMap_EachDeclaredAccessibility_MapsToItsCoreKeyword(string keyword, CoreAccessibility expected)
    {
        // The six-way table, read off a nested type so every keyword — private and protected included — is
        // legal at the declaration site. Map and TryMap must never disagree on a mappable symbol.
        INamedTypeSymbol nested = NestedTypeSymbol(keyword);

        AccessibilityMapper.TryMap(nested, out CoreAccessibility accessibility).ShouldBeTrue();
        accessibility.ShouldBe(expected);
        AccessibilityMapper.Map(nested).ShouldBe(expected);
    }

    private static IArrayTypeSymbol ArrayTypeSymbol()
    {
        CSharpCompilation compilation = Compilation(null);
        return compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32));
    }

    // The base type of a class deriving from a name nothing declares: Roslyn substitutes an error symbol.
    private static INamedTypeSymbol UnresolvedBaseTypeSymbol()
    {
        CSharpCompilation compilation = Compilation("namespace App;\npublic class Widget : Ghost.Base { }\n");
        INamedTypeSymbol widget = compilation.GetTypeByMetadataName("App.Widget")!;
        return widget.BaseType!;
    }

    private static INamedTypeSymbol NestedTypeSymbol(string keyword)
    {
        CSharpCompilation compilation = Compilation(
            $"namespace App;\npublic class Outer {{ {keyword} class Nested {{ }} }}\n");
        return compilation.GetTypeByMetadataName("App.Outer")!.GetTypeMembers("Nested").Single();
    }

    private static CSharpCompilation Compilation(string? source)
    {
        MetadataReference coreLib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        SyntaxTree[] trees = source is null ? [] : [CSharpSyntaxTree.ParseText(source)];
        return CSharpCompilation.Create("t", trees, [coreLib]);
    }
}

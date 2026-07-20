using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Roslyn;

/// <summary>
///     <see cref="AccessibilityMapper" /> maps Roslyn's six declared accessibilities onto Core's C#-keyword
///     names. A symbol whose <c>DeclaredAccessibility</c> is <c>NotApplicable</c> — an array type, never an
///     inventoried type or member — has no C# declaration meaning, so the mapper throws rather than guessing
///     (the fail-closed default arm).
/// </summary>
public sealed class AccessibilityMapperTests
{
    [Fact]
    public void Map_SymbolWithNotApplicableAccessibility_Throws()
    {
        // An array type symbol reports Accessibility.NotApplicable — the one value with no keyword meaning —
        // reaching the default arm via the ISymbol overload (an IArrayTypeSymbol is not an INamedTypeSymbol).
        MetadataReference coreLib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create("t", references: [coreLib]);
        IArrayTypeSymbol arrayType =
            compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32));

        Should.Throw<InvalidOperationException>(() => AccessibilityMapper.Map(arrayType));
    }
}
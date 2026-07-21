using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Tests.Correspondence;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The load-bearing correspondence pin (plan Step 2): for each type shape,
///     <see cref="TypeName.FullDisplay" /> over the reflection type must equal the FullName Roslyn
///     extraction produces for the byte-identical source. <see cref="CorrespondenceTypes" /> declares
///     the reflectable mirrors; the source below re-declares them. If either renderer drifts, a
///     <c>typeof(...)</c> in a spec silently stops matching — so both are pinned together here.
/// </summary>
public sealed class TypeNameFullDisplayTests
{
    private const string Ns = "Zphil.LoadBearing.Tests.Correspondence";

    private static readonly CodebaseModel Model = CompilationFactory.Extract($$"""
                                                                               namespace {{Ns}};
                                                                               public class Simple {}
                                                                               public class Outer { public class Inner {} }
                                                                               public interface IBox<T> {}
                                                                               public class Pair<TFirst, TSecond> {}
                                                                               public class UsesSimple : IBox<Simple> {}
                                                                               public class UsesInt : IBox<int> {}
                                                                               public class UsesNested : IBox<Outer.Inner> {}
                                                                               public class UsesArray : IBox<Simple[]> {}
                                                                               public class UsesRank2Array : IBox<Simple[,]> {}
                                                                               public class UsesGenericInGeneric : IBox<Pair<Simple, int>> {}
                                                                               """);

    [Fact]
    public void FullDisplay_NonGenericType_MatchesExtractedDefinition()
    {
        AssertExtractedDefinition(typeof(Simple), $"{Ns}.Simple");
    }

    [Fact]
    public void FullDisplay_NestedType_DotsTheDeclaringChain()
    {
        AssertExtractedDefinition(typeof(Outer.Inner), $"{Ns}.Outer.Inner");
    }

    [Fact]
    public void FullDisplay_OpenGenericDefinition_UsesDeclaredParameterName()
    {
        AssertExtractedDefinition(typeof(IBox<>), $"{Ns}.IBox<T>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithSolutionType_MatchesExtractedConstruction()
    {
        AssertExtractedInterface("UsesSimple", typeof(IBox<Simple>), $"{Ns}.IBox<{Ns}.Simple>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithPrimitive_RendersSystemInt32NotKeyword()
    {
        AssertExtractedInterface("UsesInt", typeof(IBox<int>), $"{Ns}.IBox<System.Int32>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithNestedArgument_DotsTheArgumentChain()
    {
        AssertExtractedInterface("UsesNested", typeof(IBox<Outer.Inner>), $"{Ns}.IBox<{Ns}.Outer.Inner>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithArrayArgument_AppendsBrackets()
    {
        AssertExtractedInterface("UsesArray", typeof(IBox<Simple[]>), $"{Ns}.IBox<{Ns}.Simple[]>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithRank2ArrayArgument_AppendsRankedBrackets()
    {
        // A rank-2 array renders `[,]`, not `[]` (the rank must not be dropped) — and it must still match the
        // form Roslyn extraction produces for the byte-identical source.
        AssertExtractedInterface("UsesRank2Array", typeof(IBox<Simple[,]>), $"{Ns}.IBox<{Ns}.Simple[,]>");
    }

    [Fact]
    public void FullDisplay_ClosedGenericWithGenericArgument_QualifiesRecursively()
    {
        AssertExtractedInterface(
            "UsesGenericInGeneric",
            typeof(IBox<Pair<Simple, int>>),
            $"{Ns}.IBox<{Ns}.Pair<{Ns}.Simple, System.Int32>>");
    }

    [Fact]
    public void FullDisplay_GlobalNamespaceType_RendersBareName()
    {
        CodebaseModel model = CompilationFactory.Extract("public class TopLevel {}");
        model.Types.ShouldContain(t => t.FullName == "TopLevel");
    }

    // The three type shapes with no source-level extraction analog throw (Prose/TypeName.cs:56,66): a by-ref
    // (int&) and a pointer (int*) at line 56, and a partially-open construction (Dictionary<int, TValue> — some
    // arguments bound, some free) at line 66. A discriminant selects the case so the computed types never
    // round-trip through xUnit's theory-data serializer (a by-ref/pointer/partial-open Type does not).
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FullDisplay_UnrepresentableType_ThrowsUnrepresentableTypeException(int which)
    {
        Type type = which switch
        {
            0 => typeof(int).MakeByRefType(),
            1 => typeof(int).MakePointerType(),
            _ => typeof(Dictionary<,>).MakeGenericType(typeof(int), typeof(Dictionary<,>).GetGenericArguments()[1])
        };

        Should.Throw<UnrepresentableTypeException>(() => TypeName.FullDisplay(type));
    }

    // FullDisplay must equal the pinned literal AND be a name extraction actually produced for the
    // definition — the pair proves the two renderers agree without either side asserting itself.
    private static void AssertExtractedDefinition(Type type, string expected)
    {
        TypeName.FullDisplay(type).ShouldBe(expected);
        Model.Types.ShouldContain(t => t.FullName == expected);
    }

    private static void AssertExtractedInterface(string userTypeName, Type constructedInterface, string expected)
    {
        TypeName.FullDisplay(constructedInterface).ShouldBe(expected);
        Model.Type($"{Ns}.{userTypeName}").AllInterfaces.Select(c => c.FullName).ShouldContain(expected);
    }
}
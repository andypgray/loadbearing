using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     <see cref="TypeNode.SymbolId" /> extraction over the fast path: the Roslyn
///     <c>DocumentationCommentId</c> of the original definition (the baseline key, GRAMMAR §4.3) —
///     a plain type, an open generic (backtick-arity, not <c>&lt;T&gt;</c>), a nested type (dotted),
///     and an external BCL target. Unlike <see cref="TypeNode.FullName" />, the ID is DocID-shaped.
/// </summary>
public sealed class SymbolIdExtractionTests
{
    [Fact]
    public void PlainType_HasTypeDocId()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Type {}
                                                         """);

        model.Type("N.Type").SymbolId.ShouldBe("T:N.Type");
    }

    [Fact]
    public void OpenGeneric_UsesBacktickArityNotAngleBrackets()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         """);

        // FullName renders `<T>`; the DocID-shaped SymbolId renders `1.
        TypeNode handler = model.Type("N.IHandler<T>");
        handler.SymbolId.ShouldBe("T:N.IHandler`1");
    }

    [Fact]
    public void NestedType_UsesDottedContainingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Outer { public class Inner {} }
                                                         """);

        model.Type("N.Outer.Inner").SymbolId.ShouldBe("T:N.Outer.Inner");
    }

    [Fact]
    public void ExternalType_CarriesTypeDocId()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System.Text;
                                                         namespace N;
                                                         public class C { public StringBuilder Make() => new StringBuilder(); }
                                                         """);

        TypeNode stringBuilder = model.Type("System.Text.StringBuilder");
        stringBuilder.IsExternal.ShouldBeTrue();
        stringBuilder.SymbolId.ShouldBe("T:System.Text.StringBuilder");
    }
}
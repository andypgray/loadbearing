using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Type-node facts over the fast path: kind mapping, identity (FullName / nested / open generic),
///     partial merge, and the <see cref="ITypeInfo" /> hierarchy surface (base type, interfaces,
///     attributes).
/// </summary>
public sealed class CodebaseExtractorTypeTests
{
    [Fact]
    public void ExtractFromCompilations_Record_MapsToClassKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record R(int X);
                                                         """);

        model.Type("N.R").Kind.ShouldBe(TypeKind.Class);
    }

    [Fact]
    public void ExtractFromCompilations_RecordStruct_MapsToStructKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record struct RS(int X);
                                                         """);

        model.Type("N.RS").Kind.ShouldBe(TypeKind.Struct);
    }

    [Fact]
    public void ExtractFromCompilations_StaticClass_MapsToClassKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public static class SC {}
                                                         """);

        model.Type("N.SC").Kind.ShouldBe(TypeKind.Class);
    }

    [Fact]
    public void ExtractFromCompilations_Enum_MapsToEnumKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public enum E { A, B }
                                                         """);

        model.Type("N.E").Kind.ShouldBe(TypeKind.Enum);
    }

    [Fact]
    public void ExtractFromCompilations_Delegate_MapsToDelegateKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void D();
                                                         """);

        model.Type("N.D").Kind.ShouldBe(TypeKind.Delegate);
    }

    [Fact]
    public void ExtractFromCompilations_Interface_MapsToInterfaceKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface I {}
                                                         """);

        model.Type("N.I").Kind.ShouldBe(TypeKind.Interface);
    }

    [Fact]
    public void ExtractFromCompilations_Struct_MapsToStructKind()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public struct S {}
                                                         """);

        model.Type("N.S").Kind.ShouldBe(TypeKind.Struct);
    }

    [Fact]
    public void ExtractFromCompilations_NestedType_IsDistinctNodeWithDottedFullName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Outer { public class Inner {} }
                                                         """);

        TypeNode inner = model.Type("N.Outer.Inner");
        inner.Name.ShouldBe("Inner");
        inner.Namespace.ShouldBe("N");
        model.Types.ShouldContain(t => t.FullName == "N.Outer");
    }

    [Fact]
    public void ExtractFromCompilations_OpenGeneric_FullNameUsesDeclaredTypeParameterNames()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public interface IDict<TKey, TValue> {}
                                                         """);

        model.Types.ShouldContain(t => t.FullName == "N.IHandler<T>");
        model.Types.ShouldContain(t => t.FullName == "N.IDict<TKey, TValue>");
    }

    [Fact]
    public void ExtractFromCompilations_PartialClass_MergesToOneNodeWithBothDeclarationSites()
    {
        CodebaseModel model = CompilationFactory.Extract(
            "N",
            ("PartA.cs", """
                         namespace N;
                         public partial class P
                         {
                             public int A;
                         }
                         """),
            ("PartB.cs", """
                         namespace N;
                         public partial class P
                         {
                             public int B;
                         }
                         """));

        TypeNode p = model.Type("N.P");
        p.DeclarationSites.Select(s => (s.FilePath, s.Line))
            .ShouldBe([("PartA.cs", 2), ("PartB.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_PartialClass_MergesEdgeSitesAcrossParts()
    {
        CodebaseModel model = CompilationFactory.Extract(
            "N",
            ("PartA.cs", """
                         namespace N;
                         public class Dep {}
                         public partial class P { public Dep FromA; }
                         """),
            ("PartB.cs", """
                         namespace N;
                         public partial class P { public Dep FromB; }
                         """));

        ReferenceEdge edge = model.Edge("N.P", "N.Dep");
        edge.Files().ShouldBe(["PartA.cs", "PartB.cs"]);
        edge.Lines().ShouldBe([3, 2]);
    }

    [Fact]
    public void ExtractFromCompilations_DeclarationSite_UsesIdentifierLineNotAttributeLine()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class MarkAttribute : Attribute {}
                                                         [Mark]
                                                         public class Decorated {}
                                                         """);

        model.Type("N.Decorated").DeclarationSites.Single().Line.ShouldBe(5);
    }

    [Fact]
    public void ExtractFromCompilations_GlobalNamespaceType_HasEmptyNamespace()
    {
        CodebaseModel model = CompilationFactory.Extract("public class TopLevel {}");

        TypeNode top = model.Type("TopLevel");
        top.Namespace.ShouldBe("");
    }

    [Fact]
    public void ExtractFromCompilations_ExplicitBaseType_PopulatesBaseTypeAndEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base {}
                                                         public class Derived : Base {}
                                                         """);

        model.Type("N.Derived").BaseType!.FullName().ShouldBe("N.Base");
        model.HasEdge("N.Derived", "N.Base").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_ImplicitObjectBase_PopulatesBaseTypeWithoutEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Plain {}
                                                         """);

        var baseType = (TypeNode)model.Type("N.Plain").BaseType!;
        baseType.FullName.ShouldBe("System.Object");
        baseType.IsExternal.ShouldBeTrue();
        model.HasEdge("N.Plain", "System.Object").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_InterfaceNode_HasNullBaseType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface I {}
                                                         """);

        model.Type("N.I").BaseType.ShouldBeNull();
    }

    [Fact]
    public void ExtractFromCompilations_DirectInterfaces_PopulateInterfacesList()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IA {}
                                                         public interface IB {}
                                                         public class C : IA, IB {}
                                                         """);

        model.Type("N.C").Interfaces.Select(i => i.FullName())
            .ShouldBe(["N.IA", "N.IB"], true);
    }

    [Fact]
    public void ExtractFromCompilations_AttributesList_ContainsAttributeClassNodes()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class TagAttribute : Attribute {}
                                                         [Tag]
                                                         public class C {}
                                                         """);

        model.Type("N.C").Attributes.Select(a => a.FullName())
            .ShouldContain("N.TagAttribute");
    }

    [Fact]
    public void ExtractFromCompilations_SelfReferentialAttribute_Builds()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         [Self]
                                                         public sealed class SelfAttribute : Attribute {}
                                                         """);

        model.Type("N.SelfAttribute").Attributes.Select(a => a.FullName())
            .ShouldContain("N.SelfAttribute");
    }

    [Fact]
    public void ExtractFromCompilations_CompilerGeneratedTypes_AreSkipped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public Func<int> M(int x) => () => x + 1;
                                                         }
                                                         """);

        // The captured-lambda display class is synthesized at emit time and never a source symbol;
        // no `<...>`-named node may leak into the model.
        model.Types.ShouldAllBe(t => !t.Name.StartsWith("<"));
        model.Types.ShouldContain(t => t.FullName == "N.C");
    }
}
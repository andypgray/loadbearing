using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     One fact per edge-producing (or edge-suppressing) construct, over the MSBuild-free fast path.
///     Line numbers are 1-based and count from the first content line of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_BaseListEntry_ProducesEdgeAtBaseListLine()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class A : B {}
                                                         public class B {}
                                                         """);

        model.Edge("N.A", "N.B").Lines().ShouldBe([2]);
    }

    [Fact]
    public void ExtractFromCompilations_InterfaceImplementation_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface I {}
                                                         public class C : I {}
                                                         """);

        model.Edge("N.C", "N.I").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_MethodSignatureParameterAndReturnTypes_ProduceEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class R {}
                                                         public class P {}
                                                         public class S { public R M(P p) => null; }
                                                         """);

        model.Edge("N.S", "N.R").Lines().ShouldBe([4]);
        model.Edge("N.S", "N.P").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_FieldAndPropertyTypes_ProduceEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class F {}
                                                         public class G {}
                                                         public class H { private F f; public G Prop { get; set; } }
                                                         """);

        model.Edge("N.H", "N.F").Lines().ShouldBe([4]);
        model.Edge("N.H", "N.G").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_Attribute_ProducesEdgeToAttributeClass()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class MyAttribute : Attribute {}
                                                         [My]
                                                         public class Decorated {}
                                                         """);

        model.Edge("N.Decorated", "N.MyAttribute").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_GenericName_ProducesEdgesToDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System.Collections.Generic;
                                                         namespace N;
                                                         public class Item {}
                                                         public class Holder { public List<Item> Items = null; }
                                                         """);

        model.HasEdge("N.Holder", "System.Collections.Generic.List<T>").ShouldBeTrue();
        model.HasEdge("N.Holder", "N.Item").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_TypeofOperand_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class T {}
                                                         public class U { public object M() => typeof(T); }
                                                         """);

        model.Edge("N.U", "N.T").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_CastExpression_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base {}
                                                         public class Derived : Base {}
                                                         public class Caster { public object M(Base b) => (Derived)b; }
                                                         """);

        model.Edge("N.Caster", "N.Derived").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_ObjectCreation_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Thing {}
                                                         public class Maker { public object M() => new Thing(); }
                                                         """);

        model.Edge("N.Maker", "N.Thing").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_ImplicitObjectCreation_ProducesEdgeToCreatedType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Widget {}
                                                         public static class Factory
                                                         {
                                                             public static void Take(Widget w) {}
                                                             public static void Call() => Take(new());
                                                         }
                                                         """);

        // Line 6 (the target-typed new()) is the only site contributed purely by implicit creation.
        model.Edge("N.Factory", "N.Widget").Lines().ShouldContain(6);
    }

    [Fact]
    public void ExtractFromCompilations_StaticMemberAccess_ProducesSingleDedupedSite()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public static class Config { public static int Value; public static void Go() {} }
                                                         public class User { public int M() { Config.Go(); return Config.Value; } }
                                                         """);

        ReferenceEdge edge = model.Edge("N.User", "N.Config");
        edge.Sites.Count.ShouldBe(1);
        edge.Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_InstanceInvocation_ProducesEdgeToContainingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Service { public void Do() {} }
                                                         public class Client
                                                         {
                                                             public void M(Service s)
                                                             {
                                                                 s.Do();
                                                             }
                                                         }
                                                         """);

        model.Edge("N.Client", "N.Service").Lines().ShouldContain(7);
    }

    [Fact]
    public void ExtractFromCompilations_ExtensionMethodCall_ProducesEdgeToDeclaringStaticClass()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public static class Ext { public static int Twice(this int x) => x * 2; }
                                                         public class Caller { public int M(int n) => n.Twice(); }
                                                         """);

        model.Edge("N.Caller", "N.Ext").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_PropertyAccess_ProducesEdgeToContainingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Model { public int Count { get; set; } }
                                                         public class Reader
                                                         {
                                                             public int M(Model m)
                                                             {
                                                                 return m.Count;
                                                             }
                                                         }
                                                         """);

        model.Edge("N.Reader", "N.Model").Lines().ShouldContain(7);
    }

    [Fact]
    public void ExtractFromCompilations_MethodGroupConversion_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public static class Handlers { public static void Handle() {} }
                                                         public class Wire { public Action M() => Handlers.Handle; }
                                                         """);

        model.Edge("N.Wire", "N.Handlers").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_NameofOperand_ProducesEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Target {}
                                                         public class Namer { public string M() => nameof(Target); }
                                                         """);

        model.Edge("N.Namer", "N.Target").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_SelfReference_ProducesNoEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Recursive { public Recursive Next; public Recursive Make() => new Recursive(); }
                                                         """);

        model.HasEdge("N.Recursive", "N.Recursive").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_OwnMemberAccess_ProducesNoEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Widget { public int Value; public int M() => this.Value + Value; }
                                                         """);

        model.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_UnresolvedName_IsSkipped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public void M() { DoesNotExist.Foo(); } }
                                                         """);

        model.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_VarKeyword_ProducesNoInferenceEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Thing { public Other Get() => null; }
                                                         public class Other {}
                                                         public class C { public void M(Thing t) { var x = t.Get(); } }
                                                         """);

        // `var` binds to Other by inference; it must not produce an edge to the inferred type.
        model.HasEdge("N.C", "N.Other").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_PredefinedType_ProducesNoEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public int Add(int a, int b) => a + b; }
                                                         """);

        model.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_UsingDirective_IsNotAttributedToAnyType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System.Text;
                                                         namespace N;
                                                         public class C { public int X; }
                                                         """);

        model.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ReferenceInsideNestedType_AttributesToNestedNotOuter()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Dep {}
                                                         public class Outer
                                                         {
                                                             public class Inner
                                                             {
                                                                 public Dep D;
                                                             }
                                                         }
                                                         """);

        model.HasEdge("N.Outer.Inner", "N.Dep").ShouldBeTrue();
        model.HasEdge("N.Outer", "N.Dep").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_RepeatedReferenceOnOneLine_DedupesToOneSite()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Dep { public static void A() {} public static void B() {} }
                                                         public class C { public void M() { Dep.A(); Dep.B(); } }
                                                         """);

        model.Edge("N.C", "N.Dep").Sites.Count.ShouldBe(1);
    }

    [Fact]
    public void ExtractFromCompilations_EdgesAndSites_AreOrdinallyOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Beta {}
                                                         public class Alpha {}
                                                         public class C
                                                         {
                                                             public Beta First(Alpha a) => null;
                                                             public Alpha Second(Beta b) => null;
                                                         }
                                                         """);

        model.Edges.Select(e => (e.Source.FullName, e.Target.FullName))
            .ShouldBe([("N.C", "N.Alpha"), ("N.C", "N.Beta")]);
        model.Edge("N.C", "N.Alpha").Lines().ShouldBe([6, 7]);
    }

    [Fact]
    public void ExtractFromCompilations_CrossCompilationReference_UnifiesToDeclaredNode()
    {
        CompilationInput a = CompilationFactory.Compile("A", ("A.cs", """
                                                                      namespace A;
                                                                      public class Foo {}
                                                                      """));
        CompilationInput b = CompilationFactory.CompileReferencing("B", a.Compilation, "A", ("B.cs", """
                                                                                                     namespace B;
                                                                                                     public class Bar { public A.Foo F; }
                                                                                                     """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([a, b]);

        TypeNode target = model.Edge("B.Bar", "A.Foo").Target;
        target.IsExternal.ShouldBeFalse();
        target.ProjectName.ShouldBe("A");
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_CollapsesDuplicateEdges()
    {
        // Two compilations sharing a project name and source path model one project's two TFMs.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            public class B { public A Ref; }
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file);
        CompilationInput second = CompilationFactory.Compile("P", file);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Edges.Count(e => e.Source.FullName == "P.B" && e.Target.FullName == "P.A").ShouldBe(1);
        model.Types.Count(t => t.FullName == "P.A").ShouldBe(1);
        model.Edge("P.B", "P.A").Sites.Count.ShouldBe(1);
    }

    [Fact]
    public void ExtractFromCompilations_ExternalTarget_IsFlaggedExternalWithAssemblyProjectName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public System.Exception E; }
                                                         """);

        TypeNode target = model.Edge("N.C", "System.Exception").Target;
        target.IsExternal.ShouldBeTrue();
        target.ProjectName.ShouldNotBeNullOrEmpty();
    }
}
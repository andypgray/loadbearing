using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     One fact per construction construct (GRAMMAR §4.5), over the MSBuild-free fast path — the ctor analog
///     of <see cref="CodebaseExtractorEdgeTests" /> and <see cref="CodebaseExtractorMemberEdgeTests" />. Each
///     minting row also asserts the co-existing type-reference edge is STILL present (an explicit <c>new Foo()</c>
///     rides the walker with a null type channel, so a naive "type channel null ⇒ skip" guard would silently
///     drop its ctor edge while the type edge survived — these rows are what catch that). Line numbers are
///     1-based and count from the first content line of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorConstructorEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_ExplicitObjectCreation_MintsConstructorEdgeAtNewSite()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Thing {}
                                                         public class Maker { public object M() => new Thing(); }
                                                         """);

        model.ConstructorEdge("N.Maker", "N.Thing").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_ExplicitObjectCreation_CoexistsWithTypeReferenceEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Thing {}
                                                         public class Maker { public object M() => new Thing(); }
                                                         """);

        // Recorded BESIDE the type edge, never instead of it: the inner `Thing` name mints the §4.1 reference,
        // the `new` node mints the construction. Both stand at line 3.
        model.HasConstructorEdge("N.Maker", "N.Thing").ShouldBeTrue();
        model.HasEdge("N.Maker", "N.Thing").ShouldBeTrue();
        model.Edge("N.Maker", "N.Thing").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_TargetTypedNew_MintsSameConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Widget {}
                                                         public class Maker { public Widget M() { Widget w = new(); return w; } }
                                                         """);

        model.ConstructorEdge("N.Maker", "N.Widget").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_TargetTypedNew_MintsCtorEdgeAndTheImplicitOnlyTypeEdge()
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

        // Line 6 (the target-typed new()) is the sole node contributing the Widget type edge purely from
        // implicit creation (there is no inner type-name syntax) — and it mints the ctor edge there too.
        model.ConstructorEdge("N.Factory", "N.Widget").Lines().ShouldBe([6]);
        model.Edge("N.Factory", "N.Widget").Lines().ShouldContain(6);
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGeneric_NormalizesToOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Box<T> {}
                                                         public class Maker { public object M() => new Box<int>(); }
                                                         """);

        // new Box<int>() records the OPEN definition N.Box<T> (§4.1), and the co-existing type edge too.
        model.ConstructorEdge("N.Maker", "N.Box<T>").Lines().ShouldBe([3]);
        model.HasEdge("N.Maker", "N.Box<T>").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_ExternalConstructedType_MintsCtorEdgeFlaggedExternal()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Maker { public object M() => new System.Text.StringBuilder(); }
                                                         """);

        ConstructorEdge edge = model.ConstructorEdge("N.Maker", "System.Text.StringBuilder");
        edge.Constructed.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([2]);
    }

    [Fact]
    public void ExtractFromCompilations_AttributeApplication_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class MyAttribute : Attribute {}
                                                         [My]
                                                         public class Decorated {}
                                                         """);

        // An attribute application is not an object-creation expression: the type edge rides, no ctor edge.
        model.HasConstructorEdge("N.Decorated", "N.MyAttribute").ShouldBeFalse();
        model.HasEdge("N.Decorated", "N.MyAttribute").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_BaseAndThisInitializers_ProduceNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base { public Base(int x) {} }
                                                         public class Derived : Base
                                                         {
                                                             public Derived() : base(1) {}
                                                             public Derived(int x) : this() {}
                                                         }
                                                         """);

        // `: base(...)` and `: this(...)` are constructor initializers, not object-creation expressions.
        model.HasConstructorEdge("N.Derived", "N.Base").ShouldBeFalse();
        model.HasConstructorEdge("N.Derived", "N.Derived").ShouldBeFalse();
        model.HasEdge("N.Derived", "N.Base").ShouldBeTrue(); // the base-list reference still stands
    }

    [Fact]
    public void ExtractFromCompilations_DelegateCreation_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void Notify();
                                                         public class Wire
                                                         {
                                                             public Notify M() => new Notify(Handler);
                                                             private static void Handler() {}
                                                         }
                                                         """);

        // Delegate creation is excluded, keyed on the created symbol's TypeKind.Delegate — the type edge rides.
        model.HasConstructorEdge("N.Wire", "N.Notify").ShouldBeFalse();
        model.HasEdge("N.Wire", "N.Notify").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_TargetTypedDelegateCreation_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void Notify();
                                                         public class Wire
                                                         {
                                                             public Notify M() { Notify n = new(Handler); return n; }
                                                             private static void Handler() {}
                                                         }
                                                         """);

        // The delegate skip is symbol-keyed, so the target-typed spelling is excluded too.
        model.HasConstructorEdge("N.Wire", "N.Notify").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_WithExpression_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record Point(int X, int Y);
                                                         public class Mutator { public Point M(Point p) => p with { X = 1 }; }
                                                         """);

        // A `with` expression is not an object-creation expression (walk boundary) — no ctor edge.
        model.HasConstructorEdge("N.Mutator", "N.Point").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_ArrayCreation_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Item {}
                                                         public class Alloc { public object M() => new Item[10]; }
                                                         """);

        // Array creation is not object creation: the element type edge rides, but no ctor edge.
        model.HasConstructorEdge("N.Alloc", "N.Item").ShouldBeFalse();
        model.HasEdge("N.Alloc", "N.Item").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_SelfConstruction_ProducesNoConstructorEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Recursive { public Recursive Make() => new Recursive(); }
                                                         """);

        // Self-construction is dropped, mirroring the type-edge self-drop the walker already applies (§4.1).
        model.HasConstructorEdge("N.Recursive", "N.Recursive").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_RepeatedConstructionOnOneLine_DedupesToOneSite()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Thing {}
                                                         public class Maker { public object M() { var a = new Thing(); var b = new Thing(); return a; } }
                                                         """);

        model.ConstructorEdge("N.Maker", "N.Thing").Sites.Count.ShouldBe(1);
    }

    [Fact]
    public void ExtractFromCompilations_ConstructorEdges_AreOrdinallyOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Beta {}
                                                         public class Alpha {}
                                                         public class C
                                                         {
                                                             public object First() => new Beta();
                                                             public object Second() => new Alpha();
                                                         }
                                                         """);

        model.ConstructorEdges.Select(e => (e.Source.FullName, e.Constructed.FullName))
            .ShouldBe([("N.C", "N.Alpha"), ("N.C", "N.Beta")]);
    }
}
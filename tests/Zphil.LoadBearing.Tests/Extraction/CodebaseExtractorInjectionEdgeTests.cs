using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Constructor-injection edges (GRAMMAR §4.7), over the MSBuild-free fast path. One fact per
///     edge-producing (or edge-suppressing) constructor shape: explicit and primary constructors on a class
///     and a record; definition-level parameter-type decomposition (constructed generic, array,
///     <c>IEnumerable&lt;T&gt;</c>); external endpoints; and the must-NOT-mint rows (static/implicit/copy
///     constructors, self-injection, enum/delegate sources). Line numbers count from the first content line
///     of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorInjectionEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_ExplicitConstructorOnClass_ProducesInjectionEdgeAtParameterLine()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class Svc { public Svc(IDep d) {} }
                                                         """);

        model.InjectionEdge("N.Svc", "N.IDep").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_PrimaryConstructorOnClass_ProducesInjectionEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class Svc(IDep d) { }
                                                         """);

        model.InjectionEdge("N.Svc", "N.IDep").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_ExplicitConstructorOnRecord_ProducesInjectionEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public record Rec { public Rec(IDep d) {} }
                                                         """);

        model.InjectionEdge("N.Rec", "N.IDep").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_PrimaryConstructorOnRecord_ProducesOnlyTheParameterEdgeNoCopyCtorSelfEdge()
    {
        // The positional record's primary constructor mints the parameter edge; its compiler-generated copy
        // constructor (Rec(Rec)) is implicitly declared, so it mints nothing — and would only ever be a
        // self-edge, which is dropped regardless. So exactly one injection edge, to IDep.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public record Rec(IDep D);
                                                         """);

        model.InjectionEdges("N.Rec").Select(e => e.Injected.FullName).ShouldBe(["N.IDep"]);
        model.HasInjectionEdge("N.Rec", "N.Rec").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericParameter_DecomposesToDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class Order {}
                                                         public class C { public C(IHandler<Order> h) {} }
                                                         """);

        model.HasInjectionEdge("N.C", "N.IHandler<T>").ShouldBeTrue();
        model.HasInjectionEdge("N.C", "N.Order").ShouldBeTrue();
        model.InjectionEdge("N.C", "N.IHandler<T>").Lines().ShouldBe([4]);
        model.InjectionEdge("N.C", "N.Order").Lines().ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_ArrayParameter_DecomposesToElementType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class C { public C(IDep[] deps) {} }
                                                         """);

        // The element type is the endpoint; the array type itself is not a node.
        model.InjectionEdges("N.C").Select(e => e.Injected.FullName).ShouldBe(["N.IDep"]);
    }

    [Fact]
    public void ExtractFromCompilations_EnumerableOfInterfaceParameter_DecomposesToDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IFoo {}
                                                         public class C { public C(System.Collections.Generic.IEnumerable<IFoo> foos) {} }
                                                         """);

        model.HasInjectionEdge("N.C", "System.Collections.Generic.IEnumerable<T>").ShouldBeTrue();
        model.HasInjectionEdge("N.C", "N.IFoo").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_ExternalParameterType_GetsAMatchableExternalNode()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public C(System.IDisposable d) {} }
                                                         """);

        InjectionEdge edge = model.InjectionEdge("N.C", "System.IDisposable");
        edge.Injected.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([2]);
    }

    [Fact]
    public void ExtractFromCompilations_StaticConstructor_ProducesNoInjectionEdge()
    {
        // A static constructor is not an instance constructor, and the implicit parameterless instance
        // constructor is filtered — so neither mints an injection edge.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class WithStaticCtor { static WithStaticCtor() {} }
                                                         """);

        model.InjectionEdges("N.WithStaticCtor").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ImplicitParameterlessConstructor_ProducesNoInjectionEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Bare {}
                                                         """);

        model.InjectionEdges("N.Bare").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_SelfInjection_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Selfish { public Selfish(Selfish other) {} }
                                                         """);

        model.HasInjectionEdge("N.Selfish", "N.Selfish").ShouldBeFalse();
        model.InjectionEdges("N.Selfish").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_EnumAndDelegateSources_ProduceNoInjectionEdges()
    {
        // Enum and delegate types declare no walkable instance constructors, so they are the source of no
        // injection edge (the delegate's Invoke parameter and the enum's members are not constructor params).
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void Notify(int x);
                                                         public enum Color { Red }
                                                         """);

        model.InjectionEdges("N.Notify").ShouldBeEmpty();
        model.InjectionEdges("N.Color").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TwoParametersOfSameType_UnionsToTwoSites()
    {
        // Two constructor parameters of the same injected type record two distinct sites (one per parameter
        // line), unioned under the one (source, injected) edge.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class Svc
                                                         {
                                                             public Svc(
                                                                 IDep a,
                                                                 IDep b) {}
                                                         }
                                                         """);

        model.InjectionEdge("N.Svc", "N.IDep").Lines().ShouldBe([6, 7]);
    }
}
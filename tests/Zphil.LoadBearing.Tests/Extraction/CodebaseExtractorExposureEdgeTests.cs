using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     Signature-exposure edges (GRAMMAR §4.9), over the MSBuild-free fast path — the exposure analog of
///     <see cref="CodebaseExtractorInjectionEdgeTests" />. One fact per signature position of an
///     effectively-public member: a method's return and parameter types, a property/field/event type. Covers
///     the decomposition rule (constructed generic, array), the type-parameter and void gates, and the whole
///     must-NOT-mint set: the constructor axis (§4.7), base/interface lists (inheritance), a non-public member,
///     an internal type, a public member nested in an internal type (the effective-visibility pin), an explicit
///     interface implementation, record synthesized members, self-exposure, and enum value self-typing. Line
///     numbers count from the first content line of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorExposureEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_ReturnType_MintsExposureEdgeBesideTheReferenceEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public Gadget Make() => null; }
                                                         """);

        // The return type's name syntax mints the §4.1 reference edge; the exposure channel rides beside it.
        model.ExposureEdge("N.C", "N.Gadget").Lines().ShouldBe([3]);
        model.HasEdge("N.C", "N.Gadget").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_ParameterType_MintsExposureEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public void Take(Gadget g) {} }
                                                         """);

        // The void return is skipped; the parameter type surfaces at the member's declaration line.
        model.ExposureEdges("N.C").Select(e => e.Exposed.FullName).ShouldBe(["N.Gadget"]);
        model.ExposureEdge("N.C", "N.Gadget").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_PropertyType_MintsExposureEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public Gadget Thing { get; set; } }
                                                         """);

        model.ExposureEdge("N.C", "N.Gadget").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_FieldType_MintsExposureEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public Gadget Field; }
                                                         """);

        model.ExposureEdge("N.C", "N.Gadget").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_EventType_MintsExposureEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void Notify();
                                                         public class C { public event Notify Ev; }
                                                         """);

        model.ExposureEdge("N.C", "N.Notify").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericReturn_MintsDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Order {}
                                                         public class C { public System.Threading.Tasks.Task<Order> Get() => null; }
                                                         """);

        // Task<Order> decomposes to the open definition AND the type argument, both at the member's line.
        model.HasExposureEdge("N.C", "System.Threading.Tasks.Task<TResult>").ShouldBeTrue();
        model.HasExposureEdge("N.C", "N.Order").ShouldBeTrue();
        model.ExposureEdge("N.C", "System.Threading.Tasks.Task<TResult>").Lines().ShouldBe([3]);
        model.ExposureEdge("N.C", "N.Order").Lines().ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_ArrayReturn_UnwrapsToElementType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public Gadget[] Many() => null; }
                                                         """);

        // The element type is the endpoint; the array type itself is not a node.
        model.ExposureEdges("N.C").Select(e => e.Exposed.FullName).ShouldBe(["N.Gadget"]);
    }

    [Fact]
    public void ExtractFromCompilations_TupleReturn_DecomposesToValueTupleDefinitionAndEachElement()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Order {}
                                                         public class Widget {}
                                                         public class C { public (Order, Widget) Pair() => default; }
                                                         """);

        // A tuple return decomposes definition-level exactly like any constructed generic (§4.1/§4.9): the open
        // System.ValueTuple<T1, T2> definition PLUS each element type. The open ValueTuple definition renders in
        // C# tuple syntax `(T1, T2)` — Roslyn's default display formats even the unbound ValueTuple`2 tuple-style,
        // so that (not `System.ValueTuple<T1, T2>`) is the endpoint's FullName in the model.
        model.ExposureEdges("N.C").Select(e => e.Exposed.FullName).ShouldBe(
            ["(T1, T2)", "N.Order", "N.Widget"]);

        // Reference-edge-twin observation (for the §4.9 doc reconciliation task — NOT touched here): the element
        // types DO have their ordinary §4.1 reference-edge twin (their names appear verbatim in the source), but
        // the synthesized ValueTuple wrapper `(T1, T2)` does NOT — nothing in the tuple syntax `(Order, Widget)`
        // textually names ValueTuple, so no name-driven type edge is minted for it. The exposure channel's
        // definition-level DecomposeType therefore yields an endpoint with no "recorded beside the type-level edge"
        // twin, contrary to the ExposureEdge remark's blanket wording.
        model.HasEdge("N.C", "N.Order").ShouldBeTrue();
        model.HasEdge("N.C", "N.Widget").ShouldBeTrue();
        model.HasEdge("N.C", "(T1, T2)").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_NullableValueTypeReturn_DecomposesToNullableDefinitionAndInt32()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public int? Maybe() => null; }
                                                         """);

        // A nullable value type decomposes to the open System.Nullable<T> definition PLUS its Int32 argument (the
        // same definition form a T? parameter records on the member axis, §4.6).
        model.ExposureEdges("N.C").Select(e => e.Exposed.FullName).ShouldBe(
            ["System.Int32", "System.Nullable<T>"]);

        // Reference-edge-twin observation (for the §4.9 doc reconciliation task — NOT touched here): NEITHER
        // endpoint has an ordinary §4.1 reference-edge twin — `int?` names neither System.Nullable (the wrapper is
        // synthesized by DecomposeType) nor System.Int32 (a predefined-type keyword mints no type edge; see
        // CodebaseExtractorEdgeTests.ExtractFromCompilations_PredefinedType_ProducesNoEdge). So both exposure
        // endpoints here are twin-less, again contrary to the ExposureEdge remark's blanket "recorded beside"
        // wording.
        model.HasEdge("N.C", "System.Nullable<T>").ShouldBeFalse();
        model.HasEdge("N.C", "System.Int32").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_OwnTypeParameter_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public T Echo<T>(T value) => value; }
                                                         """);

        // A method's own type parameter is not a named type, so return T and parameter T mint nothing (and the
        // decomposition never crashes on the type-parameter symbols).
        model.ExposureEdges("N.C").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_VoidReturnNoParameters_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public void Nothing() {} }
                                                         """);

        // System.Void is skipped and there are no parameters, so a void no-arg method surfaces nothing.
        model.ExposureEdges("N.C").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_Constructor_IsExcludedFromExposure()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public class C { public C(Gadget g) {} }
                                                         """);

        // A constructor parameter is the injection axis's fact (§4.7), never an exposure edge.
        model.HasExposureEdge("N.C", "N.Gadget").ShouldBeFalse();
        model.ExposureEdges("N.C").ShouldBeEmpty();
        model.HasInjectionEdge("N.C", "N.Gadget").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_BaseAndInterfaceLists_AreNotExposureEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base {}
                                                         public interface IFoo {}
                                                         public class C : Base, IFoo {}
                                                         """);

        // Base types and implemented interfaces are inheritance (§5.2), not members — no exposure edge.
        model.ExposureEdges("N.C").ShouldBeEmpty();
        model.HasExposureEdge("N.C", "N.Base").ShouldBeFalse();
        model.HasExposureEdge("N.C", "N.IFoo").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_InternalType_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         internal class Svc { public Gadget M() => null; }
                                                         """);

        // A public member of an internal type is not surface — the internal type has no external contract.
        model.ExposureEdges("N.Svc").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_NonPublicMembers_MintNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Alpha {}
                                                         public class Beta {}
                                                         public class Gamma {}
                                                         public class Delta {}
                                                         public class Svc
                                                         {
                                                             private Alpha A() => null;
                                                             protected Beta B() => null;
                                                             internal Gamma C() => null;
                                                             public Delta D() => null;
                                                         }
                                                         """);

        // Only the public member surfaces; private/protected/internal members are not part of the contract.
        model.ExposureEdges("N.Svc").Select(e => e.Exposed.FullName).ShouldBe(["N.Delta"]);
        model.ExposureEdge("N.Svc", "N.Delta").Lines().ShouldBe([11]);
    }

    [Fact]
    public void ExtractFromCompilations_PublicMemberNestedInInternalType_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         internal class Outer
                                                         {
                                                             public class Inner { public Gadget M() => null; }
                                                         }
                                                         """);

        // The effective-visibility pin: Inner is public but its containing Outer is internal, so Inner's public
        // member is not effectively public and surfaces nothing.
        model.ExposureEdges("N.Outer.Inner").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ExplicitInterfaceImplementation_IsSkippedButTheInterfaceExposes()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Gadget {}
                                                         public interface IFoo { Gadget Bar(); }
                                                         public class Foo : IFoo { Gadget IFoo.Bar() => null; }
                                                         """);

        // The interface's own public member exposes Gadget; the explicit implementation is private, so the
        // implementing class exposes nothing.
        model.HasExposureEdge("N.IFoo", "N.Gadget").ShouldBeTrue();
        model.ExposureEdges("N.Foo").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_RecordSynthesizedMembers_AreSkipped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record Money(decimal Amount);
                                                         """);

        // Only the positional property surfaces; the synthesized equality/clone/deconstruct surface is
        // implicitly declared and excluded, so no System.Object/Boolean/Int32 leaks in.
        model.ExposureEdges("N.Money").Select(e => e.Exposed.FullName).ShouldBe(["System.Decimal"]);
        model.HasExposureEdge("N.Money", "System.Object").ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_SelfExposure_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Node { public Node Next { get; set; } }
                                                         """);

        // Self-exposure is dropped, mirroring the type-edge self-drop (§4.1).
        model.HasExposureEdge("N.Node", "N.Node").ShouldBeFalse();
        model.ExposureEdges("N.Node").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_EnumValues_SelfTypeAndAreDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public enum Color { Red, Green }
                                                         """);

        // An enum's value fields are typed as the enum itself — a self-edge — so the self-drop clears them.
        model.ExposureEdges("N.Color").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_PartialType_UnionsSitesAcrossDeclaringParts()
    {
        CodebaseModel model = CompilationFactory.Extract("P",
            ("A.cs", """
                     namespace N;
                     public class Gadget {}
                     public partial class C { public Gadget First() => null; }
                     """),
            ("B.cs", """
                     namespace N;
                     public partial class C { public Gadget Second() => null; }
                     """));

        // The two parts each contribute one exposing member, unioned under the one (source, exposed) edge with
        // two deduped sites.
        model.ExposureEdges("N.C").Count.ShouldBe(1);
        model.ExposureEdge("N.C", "N.Gadget").Sites.Count.ShouldBe(2);
    }
}
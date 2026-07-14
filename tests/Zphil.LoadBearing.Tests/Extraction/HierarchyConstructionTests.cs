using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The Phase 3 construction-preserving hierarchy facts over the fast path:
///     <see cref="TypeNode.AllInterfaces" /> (transitive, substituted closure),
///     <see cref="TypeNode.BaseTypeChain" /> (nearest-first, object terminus), and
///     <see cref="TypeNode.AttributeConstructions" /> (declared only). Definition-vs-constructed
///     FullName is the load-bearing joint the checker's <c>typeof(...)</c> matching rides on.
/// </summary>
public sealed class HierarchyConstructionTests
{
    [Fact]
    public void AllInterfaces_SubstitutedTransitivityThroughGenericBase_ContainsConstructedInterface()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class HandlerBase<T> : IHandler<T> {}
                                                         public class Order {}
                                                         public class MyHandler : HandlerBase<Order> {}
                                                         """);

        TypeConstruction handler = model.Type("N.MyHandler").AllInterfaces
            .Single(c => c.Definition.FullName == "N.IHandler<T>");
        handler.FullName.ShouldBe("N.IHandler<N.Order>");
    }

    [Fact]
    public void AllInterfaces_InterfaceExtendsInterface_IncludesTransitiveClosureOrdinalOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IA {}
                                                         public interface IB : IA {}
                                                         public class C : IB {}
                                                         """);

        model.Type("N.C").AllInterfaces.Select(c => c.FullName)
            .ShouldBe(["N.IA", "N.IB"]);
    }

    [Fact]
    public void AllInterfaces_ConstructedWithPrimitiveArgument_RendersSystemInt32NotKeyword()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class IntHandler : IHandler<int> {}
                                                         """);

        TypeConstruction handler = model.Type("N.IntHandler").AllInterfaces.Single();
        handler.Definition.FullName.ShouldBe("N.IHandler<T>");
        handler.FullName.ShouldBe("N.IHandler<System.Int32>");
    }

    [Fact]
    public void AllInterfaces_MultipleInterfaces_OrderedOrdinalByConstructedFullName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IZebra {}
                                                         public interface IApple {}
                                                         public class C : IZebra, IApple {}
                                                         """);

        model.Type("N.C").AllInterfaces.Select(c => c.FullName)
            .ShouldBe(["N.IApple", "N.IZebra"]);
    }

    [Fact]
    public void BaseTypeChain_ClassHierarchy_IsNearestFirstToObject()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class A {}
                                                         public class B : A {}
                                                         public class D : B {}
                                                         """);

        model.Type("N.D").BaseTypeChain.Select(c => c.FullName)
            .ShouldBe(["N.B", "N.A", "System.Object"]);
    }

    [Fact]
    public void BaseTypeChain_SubstitutedGenericBase_KeepsConstructedNameAndOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class HandlerBase<T> {}
                                                         public class Order {}
                                                         public class MyHandler : HandlerBase<Order> {}
                                                         """);

        TypeConstruction nearest = model.Type("N.MyHandler").BaseTypeChain[0];
        nearest.Definition.FullName.ShouldBe("N.HandlerBase<T>");
        nearest.FullName.ShouldBe("N.HandlerBase<N.Order>");
    }

    [Fact]
    public void BaseTypeChain_Struct_IsValueTypeThenObject()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public struct S {}
                                                         """);

        model.Type("N.S").BaseTypeChain.Select(c => c.FullName)
            .ShouldBe(["System.ValueType", "System.Object"]);
    }

    [Fact]
    public void BaseTypeChain_Enum_IsEnumThenValueTypeThenObject()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public enum E { A, B }
                                                         """);

        model.Type("N.E").BaseTypeChain.Select(c => c.FullName)
            .ShouldBe(["System.Enum", "System.ValueType", "System.Object"]);
    }

    [Fact]
    public void BaseTypeChain_Delegate_IsMulticastDelegateThenDelegateThenObject()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void D();
                                                         """);

        model.Type("N.D").BaseTypeChain.Select(c => c.FullName)
            .ShouldBe(["System.MulticastDelegate", "System.Delegate", "System.Object"]);
    }

    [Fact]
    public void BaseTypeChain_Interface_IsEmpty()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface I {}
                                                         """);

        model.Type("N.I").BaseTypeChain.ShouldBeEmpty();
    }

    [Fact]
    public void AttributeConstructions_DeclaredAttributes_OrderedOrdinalByConstructedFullName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class ZedAttribute : Attribute {}
                                                         public sealed class AbleAttribute : Attribute {}
                                                         [Zed]
                                                         [Able]
                                                         public class C {}
                                                         """);

        model.Type("N.C").AttributeConstructions.Select(c => c.FullName)
            .ShouldBe(["N.AbleAttribute", "N.ZedAttribute"]);
    }

    [Fact]
    public void AttributeConstructions_GenericAttribute_RendersConstructedName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class TagAttribute<T> : Attribute {}
                                                         [Tag<int>]
                                                         public class C {}
                                                         """);

        TypeConstruction tag = model.Type("N.C").AttributeConstructions.Single();
        tag.Definition.FullName.ShouldBe("N.TagAttribute<T>");
        tag.FullName.ShouldBe("N.TagAttribute<System.Int32>");
    }

    [Fact]
    public void HierarchyConstructions_ExternalNode_AreShallowlyEmpty()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Plain {}
                                                         """);

        TypeNode obj = model.Type("System.Object");
        obj.IsExternal.ShouldBeTrue();
        obj.AllInterfaces.ShouldBeEmpty();
        obj.BaseTypeChain.ShouldBeEmpty();
        obj.AttributeConstructions.ShouldBeEmpty();
    }
}
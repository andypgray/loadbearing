using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The shape-fact contract over the fast path (GRAMMAR §5.6): the modifier flags carry C#
///     declaration semantics (a static class is neither sealed nor abstract; interfaces are
///     abstract; structs/enums/delegates are sealed), accessibility maps all six declared forms,
///     <c>IsRecord</c> is the v1 record story, and <c>FilePaths</c> lists the declaration files.
///     External (metadata) nodes carry accurate scalars while their sites stay empty.
/// </summary>
public sealed class ShapeFactExtractionTests
{
    private const string Modifiers = """
                                     namespace N;
                                     public sealed class SealedClass {}
                                     public static class StaticClass {}
                                     public abstract class AbstractClass {}
                                     public class PlainClass {}
                                     public interface IFace {}
                                     public struct PlainStruct {}
                                     public enum E { A }
                                     public delegate void D();
                                     public record RecClass(int X);
                                     public record struct RecStruct(int X);
                                     public sealed record SealedRec(int X);
                                     public abstract record AbstractRec;
                                     """;

    private static readonly CodebaseModel Model = CompilationFactory.Extract(Modifiers);

    [Fact]
    public void SealedClass_ReportsSealedOnly()
    {
        TypeNode t = Model.Type("N.SealedClass");
        t.IsSealed.ShouldBeTrue();
        t.IsStatic.ShouldBeFalse();
        t.IsAbstract.ShouldBeFalse();
        t.IsRecord.ShouldBeFalse();
    }

    [Fact]
    public void StaticClass_IsNeitherSealedNorAbstract()
    {
        // THE normalization pin (source path): a static class is encoded abstract+sealed but reports
        // neither, matching C# declaration semantics.
        TypeNode t = Model.Type("N.StaticClass");
        t.IsStatic.ShouldBeTrue();
        t.IsSealed.ShouldBeFalse();
        t.IsAbstract.ShouldBeFalse();
        t.IsRecord.ShouldBeFalse();
    }

    [Fact]
    public void AbstractClass_ReportsAbstractOnly()
    {
        TypeNode t = Model.Type("N.AbstractClass");
        t.IsAbstract.ShouldBeTrue();
        t.IsSealed.ShouldBeFalse();
        t.IsStatic.ShouldBeFalse();
        t.IsRecord.ShouldBeFalse();
    }

    [Fact]
    public void PlainClass_ReportsAllFlagsFalse()
    {
        TypeNode t = Model.Type("N.PlainClass");
        t.IsSealed.ShouldBeFalse();
        t.IsStatic.ShouldBeFalse();
        t.IsAbstract.ShouldBeFalse();
        t.IsRecord.ShouldBeFalse();
    }

    [Fact]
    public void Interface_ReportsKindImpliedAbstract()
    {
        TypeNode t = Model.Type("N.IFace");
        t.IsAbstract.ShouldBeTrue();
        t.IsSealed.ShouldBeFalse();
        t.IsStatic.ShouldBeFalse();
    }

    [Fact]
    public void StructEnumDelegate_ReportKindImpliedSealed()
    {
        foreach (string fqn in new[] { "N.PlainStruct", "N.E", "N.D" })
        {
            TypeNode t = Model.Type(fqn);
            t.IsSealed.ShouldBeTrue();
            t.IsAbstract.ShouldBeFalse();
            t.IsStatic.ShouldBeFalse();
        }
    }

    [Fact]
    public void Records_ReportIsRecord_WithModifiersIntact()
    {
        TypeNode recClass = Model.Type("N.RecClass");
        recClass.IsRecord.ShouldBeTrue();
        recClass.Kind.ShouldBe(TypeKind.Class);

        TypeNode recStruct = Model.Type("N.RecStruct");
        recStruct.IsRecord.ShouldBeTrue();
        recStruct.Kind.ShouldBe(TypeKind.Struct);
        recStruct.IsSealed.ShouldBeTrue();

        TypeNode sealedRec = Model.Type("N.SealedRec");
        sealedRec.IsRecord.ShouldBeTrue();
        sealedRec.IsSealed.ShouldBeTrue();

        TypeNode abstractRec = Model.Type("N.AbstractRec");
        abstractRec.IsRecord.ShouldBeTrue();
        abstractRec.IsAbstract.ShouldBeTrue();

        Model.Type("N.PlainClass").IsRecord.ShouldBeFalse();
    }

    [Fact]
    public void Accessibility_MapsAllSixDeclaredForms()
    {
        // Private/protected nested types ARE minted — CanBeReferencedByName gates name validity, not
        // accessibility. A modifierless top-level type defaults to internal (C# default).
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class PublicType
                                                         {
                                                             public class PublicNested {}
                                                             internal class InternalNested {}
                                                             protected class ProtectedNested {}
                                                             protected internal class ProtectedInternalNested {}
                                                             private protected class PrivateProtectedNested {}
                                                             private class PrivateNested {}
                                                         }
                                                         internal class InternalType {}
                                                         class DefaultType {}
                                                         """);

        model.Type("N.PublicType").Accessibility.ShouldBe(Accessibility.Public);
        model.Type("N.PublicType.PublicNested").Accessibility.ShouldBe(Accessibility.Public);
        model.Type("N.PublicType.InternalNested").Accessibility.ShouldBe(Accessibility.Internal);
        model.Type("N.PublicType.ProtectedNested").Accessibility.ShouldBe(Accessibility.Protected);
        model.Type("N.PublicType.ProtectedInternalNested").Accessibility.ShouldBe(Accessibility.ProtectedInternal);
        model.Type("N.PublicType.PrivateProtectedNested").Accessibility.ShouldBe(Accessibility.PrivateProtected);
        model.Type("N.PublicType.PrivateNested").Accessibility.ShouldBe(Accessibility.Private);
        model.Type("N.InternalType").Accessibility.ShouldBe(Accessibility.Internal);
        model.Type("N.DefaultType").Accessibility.ShouldBe(Accessibility.Internal);
    }

    [Fact]
    public void FilePaths_SingleType_IsTheOneCompiledPath()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C {}
                                                         """);

        model.Type("N.C").FilePaths.ShouldBe(["Test.cs"]);
    }

    [Fact]
    public void FilePaths_PartialAcrossTwoFiles_OrderedAndVerbatim()
    {
        CodebaseModel model = CompilationFactory.Extract(
            "N",
            ("PartA.cs", """
                         namespace N;
                         public partial class P { public int A; }
                         """),
            ("PartB.cs", """
                         namespace N;
                         public partial class P { public int B; }
                         """));

        model.Type("N.P").FilePaths.ShouldBe(["PartA.cs", "PartB.cs"]);
    }

    [Fact]
    public void FilePaths_TwoPartsInOneFile_Deduplicate()
    {
        CodebaseModel model = CompilationFactory.Extract(
            "N",
            ("One.cs", """
                       namespace N;
                       public partial class P {}
                       public partial class P {}
                       """),
            ("Two.cs", """
                       namespace N;
                       public partial class P {}
                       """));

        TypeNode p = model.Type("N.P");
        p.DeclarationSites.Count.ShouldBe(3);
        p.FilePaths.ShouldBe(["One.cs", "Two.cs"]);
    }

    [Fact]
    public void ExternalNodes_CarryRealScalars_AndEmptyFilePaths()
    {
        // System.String must be spelled out — the `string` keyword is a PredefinedType the walker
        // deliberately drops. System.Math (via typeof) is a metadata static class; System.Attribute
        // (via the base list / hierarchy pass) is abstract.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public System.String S; public System.Type T = typeof(System.Math); }
                                                         public class A : System.Attribute {}
                                                         """);

        TypeNode str = model.Type("System.String");
        str.IsExternal.ShouldBeTrue();
        str.IsSealed.ShouldBeTrue();
        str.Accessibility.ShouldBe(Accessibility.Public);
        str.FilePaths.ShouldBeEmpty();
        str.DeclarationSites.ShouldBeEmpty();

        // The normalization pin on the metadata path, where abstract+sealed encoding is guaranteed.
        TypeNode math = model.Type("System.Math");
        math.IsStatic.ShouldBeTrue();
        math.IsSealed.ShouldBeFalse();
        math.IsAbstract.ShouldBeFalse();

        model.Type("System.Attribute").IsAbstract.ShouldBeTrue();
    }
}
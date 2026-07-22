using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     One fact per declared-member inventory rule (GRAMMAR §4.6), over the MSBuild-free fast path — the
///     member-subject analog of <see cref="CodebaseExtractorMemberEdgeTests" />. Each row asserts the
///     included <c>SymbolId</c> set and/or a member's facts: kind, return/member type (definition-level),
///     the C#-declaration-semantics flags, accessibility, and declaration sites. The ratified exclusions
///     (accessors, constructors incl. static, operators/conversions, finalizers, indexers, and every
///     compiler-generated/implicitly-declared member) are pinned by their absence; enum and delegate types
///     contribute nothing; the positional-record row is pinned <b>empirically</b>. Member sets are ordered
///     ordinal by SymbolId. Pinned strings are the spec — moving one is a deliberate act.
/// </summary>
public sealed class CodebaseExtractorMemberInventoryTests
{
    [Fact]
    public void Inventory_AllFourKinds_AreInventoriedWithKindsAndSymbolIds()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public int Field;
                                                             public int Prop { get; set; }
                                                             public event System.Action Evt;
                                                             public void Do() {}
                                                         }
                                                         """);

        TypeNode c = model.Type("N.C");

        // One entry per kind, no accessors and no auto-property / field-like-event backing field.
        c.MemberIds().ShouldBe(["E:N.C.Evt", "F:N.C.Field", "M:N.C.Do", "P:N.C.Prop"]);
        c.Member("M:N.C.Do").Kind.ShouldBe(MemberKind.Method);
        c.Member("P:N.C.Prop").Kind.ShouldBe(MemberKind.Property);
        c.Member("F:N.C.Field").Kind.ShouldBe(MemberKind.Field);
        c.Member("E:N.C.Evt").Kind.ShouldBe(MemberKind.Event);
    }

    [Fact]
    public void Inventory_MethodReturnTypes_NormalizeToDefinitionLevelFullNames()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void Nothing() {}
                                                             public System.Threading.Tasks.Task Bare() => null!;
                                                             public System.Threading.Tasks.Task<int> Generic() => null!;
                                                             public int Number() => 0;
                                                         }
                                                         """);

        TypeNode c = model.Type("N.C");

        // void → System.Void; a constructed generic erases to its definition (declared type-parameter names),
        // so `Task<int>` matches a `.Returning(typeof(Task<>))` anchor (GRAMMAR §4.6). Never the metadata form.
        c.Member("M:N.C.Nothing").ReturnTypeFullName.ShouldBe("System.Void");
        c.Member("M:N.C.Bare").ReturnTypeFullName.ShouldBe("System.Threading.Tasks.Task");
        c.Member("M:N.C.Generic").ReturnTypeFullName.ShouldBe("System.Threading.Tasks.Task<TResult>");
        c.Member("M:N.C.Number").ReturnTypeFullName.ShouldBe("System.Int32");

        // A method carries only a return type; its member-type slot is null.
        c.Member("M:N.C.Nothing").MemberTypeFullName.ShouldBeNull();
    }

    [Fact]
    public void Inventory_PropertyFieldEventTypes_CarryMemberTypeFullNameNotReturnType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public string Prop { get; set; }
                                                             public int Field;
                                                             public event System.Action Evt;
                                                         }
                                                         """);

        TypeNode c = model.Type("N.C");

        c.Member("P:N.C.Prop").MemberTypeFullName.ShouldBe("System.String");
        c.Member("P:N.C.Prop").ReturnTypeFullName.ShouldBeNull();
        c.Member("F:N.C.Field").MemberTypeFullName.ShouldBe("System.Int32");
        c.Member("E:N.C.Evt").MemberTypeFullName.ShouldBe("System.Action");
    }

    [Fact]
    public void Inventory_AutoProperty_FoldsToOnePropertyEntryNoAccessorsOrBackingField()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public int P { get; set; } }
                                                         """);

        // One P: entry — never M:get_P / M:set_P (accessors fold), and no <P>k__BackingField (implicit).
        model.Type("N.C").MemberIds().ShouldBe(["P:N.C.P"]);
    }

    [Fact]
    public void Inventory_SpecialMembers_AreAllExcluded()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public C() {}
                                                             static C() {}
                                                             ~C() {}
                                                             public static C operator +(C a, C b) => a;
                                                             public static implicit operator int(C c) => 0;
                                                             public int this[int i] => i;
                                                             public void Ordinary() {}
                                                         }
                                                         """);

        // Only the Ordinary method survives — ctor, static ctor, finalizer, operator, conversion, and indexer
        // are all excluded (the ratified §4.6 list).
        model.Type("N.C").MemberIds().ShouldBe(["M:N.C.Ordinary"]);
    }

    [Fact]
    public void Inventory_PositionalRecord_IncludesPositionalPropertyOnly_PinnedEmpirically()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record R(int X);
                                                         """);

        // EMPIRICAL PIN (GRAMMAR §4.6 compiler-generated boundary): the positional property X is present; the
        // synthesized EqualityContract / <Clone>$ / PrintMembers / ToString / Equals / GetHashCode / copy-ctor
        // / Deconstruct / == / != are all implicitly declared or non-Ordinary and excluded.
        model.Type("N.R").MemberIds().ShouldBe(["P:N.R.X"]);
    }

    [Fact]
    public void Inventory_EnumType_ContributesNoInventory()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public enum Color { Red, Green, Blue }
                                                         """);

        // An enum's fields are its values — an enum-value read stays a recorded member USE (§4.5), not inventory.
        model.Type("N.Color").Members.ShouldBeEmpty();
    }

    [Fact]
    public void Inventory_DelegateType_ContributesNoInventory()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate int Notify(string message);
                                                         """);

        // Invoke / BeginInvoke / EndInvoke are runtime plumbing, not authored surface.
        model.Type("N.Notify").Members.ShouldBeEmpty();
    }

    [Fact]
    public void Inventory_VirtualMethod_IsVirtualTrueAbstractFalse()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base { public virtual void M() {} }
                                                         """);

        MemberNode m = model.Type("N.Base").Member("M:N.Base.M");
        m.IsVirtual.ShouldBeTrue();
        m.IsAbstract.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_OverrideMethod_IsNotVirtual()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Base { public virtual void M() {} }
                                                         public class Derived : Base { public override void M() {} }
                                                         """);

        // C# declaration semantics, not IL: an override is not itself "virtual" in the authored sense.
        MemberNode m = model.Type("N.Derived").Member("M:N.Derived.M");
        m.IsVirtual.ShouldBeFalse();
        m.IsAbstract.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_AbstractMethod_IsAbstractTrueVirtualFalse()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public abstract class A { public abstract void M(); }
                                                         """);

        MemberNode m = model.Type("N.A").Member("M:N.A.M");
        m.IsAbstract.ShouldBeTrue();
        m.IsVirtual.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_InterfaceMembers_AreAbstractNotVirtual()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface I { void M(); int P { get; } }
                                                         """);

        TypeNode i = model.Type("N.I");
        i.Member("M:N.I.M").IsAbstract.ShouldBeTrue();
        i.Member("M:N.I.M").IsVirtual.ShouldBeFalse();
        i.Member("P:N.I.P").IsAbstract.ShouldBeTrue();
    }

    [Fact]
    public void Inventory_AsyncMethod_IsAsyncTrueWithTaskReturn()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public async System.Threading.Tasks.Task Go() { await System.Threading.Tasks.Task.Yield(); }
                                                             public void Sync() {}
                                                         }
                                                         """);

        MemberNode go = model.Type("N.C").Member("M:N.C.Go");
        go.IsAsync.ShouldBeTrue();
        go.ReturnTypeFullName.ShouldBe("System.Threading.Tasks.Task");
        model.Type("N.C").Member("M:N.C.Sync").IsAsync.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_Accessibility_CoversPublicInternalPrivateAndStatic()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void Pub() {}
                                                             internal void Int() {}
                                                             private void Priv() {}
                                                             public static void Stat() {}
                                                         }
                                                         """);

        TypeNode c = model.Type("N.C");
        c.Member("M:N.C.Pub").Accessibility.ShouldBe(Accessibility.Public);
        c.Member("M:N.C.Int").Accessibility.ShouldBe(Accessibility.Internal);
        c.Member("M:N.C.Priv").Accessibility.ShouldBe(Accessibility.Private);
        c.Member("M:N.C.Stat").IsStatic.ShouldBeTrue();
        c.Member("M:N.C.Pub").IsStatic.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_Fields_ConstReadonlyAndStatic_AreAllInventoried()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public const int Max = 5;
                                                             public readonly int Ro;
                                                             public static int Shared;
                                                             public int Inst;
                                                         }
                                                         """);

        TypeNode c = model.Type("N.C");
        c.MemberIds().ShouldBe(["F:N.C.Inst", "F:N.C.Max", "F:N.C.Ro", "F:N.C.Shared"]);
        c.Member("F:N.C.Max").IsStatic.ShouldBeTrue(); // const is static
        c.Member("F:N.C.Shared").IsStatic.ShouldBeTrue();
        c.Member("F:N.C.Inst").IsStatic.ShouldBeFalse();
    }

    [Fact]
    public void Inventory_GenericMethod_ReturnTypeUsesDeclaredParameterName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public T Echo<T>(T value) => value; }
                                                         """);

        // The return type is the method's own type parameter, rendered as its declared name (definition-level).
        MemberNode echo = model.Type("N.C").Members.Single(m => m.Name == "Echo");
        echo.SymbolId.ShouldBe("M:N.C.Echo``1(``0)");
        echo.ReturnTypeFullName.ShouldBe("T");
    }

    [Fact]
    public void Inventory_OverloadedMethods_CarryParameterTypedSymbolIds()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public void M(int x) {} public void M(string s) {} }
                                                         """);

        // One inventory entry per overload — the §4.3 per-subject identity substrate, mirroring the edge side.
        model.Type("N.C").MemberIds().ShouldBe(["M:N.C.M(System.Int32)", "M:N.C.M(System.String)"]);
    }

    [Fact]
    public void Inventory_PartialType_UnionsMembersOrderedBySymbolIdWithPerPartSites()
    {
        CodebaseModel model = CompilationFactory.Extract("Proj",
            ("PartA.cs", """
                         namespace N;
                         public partial class Split { public int A; public void C() {} }
                         """),
            ("PartB.cs", """
                         namespace N;
                         public partial class Split { public void B() {} }
                         """));

        TypeNode split = model.Type("N.Split");

        // Members from both parts, ordered ordinal by SymbolId (F: before M:, then B before C).
        split.MemberIds().ShouldBe(["F:N.Split.A", "M:N.Split.B", "M:N.Split.C"]);

        // Each member's declaration site is the identifier line (+1) of its own part.
        split.Member("M:N.Split.B").FilePaths.ShouldBe(["PartB.cs"]);
        split.Member("M:N.Split.B").DeclarationLines().ShouldBe([2]);
        split.Member("M:N.Split.C").FilePaths.ShouldBe(["PartA.cs"]);
        split.Member("M:N.Split.C").DeclarationLines().ShouldBe([2]);
    }

    [Fact]
    public void Inventory_MethodParameters_AreCapturedInDeclarationOrderWithDefaultValuedCounted()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void Seed(int count, System.Threading.CancellationToken cancellationToken = default) {}
                                                         }
                                                         """);

        // Declaration order is preserved ([count, cancellationToken], not sorted or reversed) and a
        // default-valued parameter is a fact like any other — it counts (GRAMMAR §4.6, §5.6).
        MemberNode seed = model.Type("N.C").Members.Single(m => m.Name == "Seed");
        seed.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe(
        [
            ("count", "System.Int32"),
            ("cancellationToken", "System.Threading.CancellationToken")
        ]);
    }

    [Fact]
    public void Inventory_ExtensionMethod_IncludesTheThisParameter()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public static class StringExtensions
                                                         {
                                                             public static string Shout(this string text) => text;
                                                         }
                                                         """);

        // The DECLARED static method's parameter list is read (never the reduced instance form), so the `this`
        // receiver parameter is a recorded fact — present in both the DocId and the parameter facts.
        MemberNode shout = model.Type("N.StringExtensions").Member("M:N.StringExtensions.Shout(System.String)");
        shout.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe([("text", "System.String")]);
    }

    [Fact]
    public void Inventory_RefInOutParameters_RecordTheUnderlyingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void Move(ref int a, in int b, out int c) { c = a + b; }
                                                         }
                                                         """);

        // ref/in/out are calling-convention modifiers, not part of the recorded parameter type — all three
        // record the underlying System.Int32.
        MemberNode move = model.Type("N.C").Members.Single(m => m.Name == "Move");
        move.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe(
        [
            ("a", "System.Int32"),
            ("b", "System.Int32"),
            ("c", "System.Int32")
        ]);
    }

    [Fact]
    public void Inventory_ParamsArrayParameter_RecordsTheArrayType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void CancelAll(params System.Threading.CancellationToken[] tokens) {}
                                                         }
                                                         """);

        // A params parameter records the ARRAY type, never the element type.
        MemberNode cancelAll = model.Type("N.C").Members.Single(m => m.Name == "CancelAll");
        cancelAll.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe(
            [("tokens", "System.Threading.CancellationToken[]")]);
    }

    [Fact]
    public void Inventory_NullableValueTypeParameter_RecordsNullableDefinitionForm()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public void Maybe(System.Threading.CancellationToken? token) {}
                                                         }
                                                         """);

        // A T? parameter records System.Nullable<T>'s definition form (never the unwrapped T), the same
        // construction-erasing normalization the return type uses.
        MemberNode maybe = model.Type("N.C").Members.Single(m => m.Name == "Maybe");
        maybe.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe([("token", "System.Nullable<T>")]);
    }

    [Fact]
    public void Inventory_TypeParameterTypedParameter_RecordsDeclaredTypeParameterName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public T Echo<T>(T value) => value; }
                                                         """);

        // The same definition-level path that renders Echo's RETURN type as "T" renders its parameter type "T".
        MemberNode echo = model.Type("N.C").Members.Single(m => m.Name == "Echo");
        echo.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe([("value", "T")]);
    }

    [Fact]
    public void Inventory_PositionalRecord_PropertyCarriesNoParameters()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public record R(int X);
                                                         """);

        // The positional list surfaces as the generated property X only; the primary constructor is outside the
        // member inventory, so nothing here carries parameter facts.
        TypeNode r = model.Type("N.R");
        r.MemberIds().ShouldBe(["P:N.R.X"]);
        r.Member("P:N.R.X").Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void Inventory_PartialMethod_ReadsParametersOnceInDeclarationOrder()
    {
        CodebaseModel model = CompilationFactory.Extract("Proj",
            ("DefPart.cs", """
                           namespace N;
                           public partial class Host { partial void OnScan(int index, string label); }
                           """),
            ("ImplPart.cs", """
                            namespace N;
                            public partial class Host { partial void OnScan(int index, string label) {} }
                            """));

        // A partial method's defining and implementing parts resolve to ONE inventory member (Single throws on
        // a duplicate), and its parameters are read once — [index, label], never doubled to four or reordered.
        MemberNode onScan = model.Type("N.Host").Members.Single(m => m.Name == "OnScan");
        onScan.Parameters.Select(p => (p.Name, p.TypeFullName)).ShouldBe(
        [
            ("index", "System.Int32"),
            ("label", "System.String")
        ]);
    }

    [Fact]
    public void Inventory_PropertyFieldEvent_CarryEmptyParameterLists()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public int Field;
                                                             public int Prop { get; set; }
                                                             public event System.Action Evt;
                                                             public void Nullary() {}
                                                         }
                                                         """);

        // Only methods carry parameters; a property, field, and event each hold the empty list, as does a
        // parameterless method.
        TypeNode c = model.Type("N.C");
        c.Member("F:N.C.Field").Parameters.ShouldBeEmpty();
        c.Member("P:N.C.Prop").Parameters.ShouldBeEmpty();
        c.Member("E:N.C.Evt").Parameters.ShouldBeEmpty();
        c.Member("M:N.C.Nullary").Parameters.ShouldBeEmpty();
    }
}
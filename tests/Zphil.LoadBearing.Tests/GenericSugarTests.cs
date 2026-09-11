using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     Generic-type sugar (GRAMMAR §5.2/§5.3): <c>arch.Type&lt;T&gt;()</c>, the
///     <c>Implementing</c>/<c>DerivedFrom</c>/<c>AttributedWith</c> adjective twins, and the
///     <c>MustImplement</c>/<c>MustDeriveFrom</c>/<c>MustBeAttributedWith</c> constraint twins each reify
///     identically to their <c>typeof</c> counterpart — the model is the sole source of prose, so identical
///     rendered sentences prove identical reification. A <c>[Fact]</c> per family: type arguments are
///     compile-time, so there is nothing to theorize. (Open generics stay <c>typeof</c> — inexpressible as
///     a type argument — and are out of scope by design.)
/// </summary>
public class GenericSugarTests
{
    [Fact]
    public void TypeSugar_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Type<SugarType>()
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Type(typeof(SugarType))
                .MustBeSealed()));
    }

    [Fact]
    public void Implementing_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.Implementing<ISugarPort>()
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Implementing(typeof(ISugarPort))
                .MustBeSealed()));
    }

    [Fact]
    public void DerivedFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.DerivedFrom<SugarBase>()
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Types.DerivedFrom(typeof(SugarBase))
                .MustBeSealed()));
    }

    [Fact]
    public void AttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.AttributedWith<SugarAttribute>()
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Types.AttributedWith(typeof(SugarAttribute))
                .MustBeSealed()));
    }

    [Fact]
    public void MustImplement_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustImplement<ISugarPort>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustImplement(typeof(ISugarPort))));
    }

    [Fact]
    public void MustDeriveFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustDeriveFrom<SugarBase>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustDeriveFrom(typeof(SugarBase))));
    }

    [Fact]
    public void MustBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustBeAttributedWith<SugarAttribute>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustBeAttributedWith(typeof(SugarAttribute))));
    }

    [Fact]
    public void MustNotImplement_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustNotImplement<ISugarPort>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustNotImplement(typeof(ISugarPort))));
    }

    [Fact]
    public void MustNotDeriveFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustNotDeriveFrom<SugarBase>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustNotDeriveFrom(typeof(SugarBase))));
    }

    [Fact]
    public void MustNotBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.MustNotBeAttributedWith<SugarAttribute>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.MustNotBeAttributedWith(typeof(SugarAttribute))));
    }

    // ---- The member attribute axis (GRAMMAR §5.7). The adjective twin is RECEIVER-typed, not TSelf-generic:
    //      C# has no partial type inference, so a TSelf-generic twin would force both type arguments at every
    //      call site. One overload per receiver — and the MethodSelection one keeps .Returning reachable ----

    [Fact]
    public void MemberAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.Properties.AttributedWith<SugarAttribute>()
                .MustBePublic())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Properties.AttributedWith(typeof(SugarAttribute))
                .MustBePublic()));
    }

    [Fact]
    public void MemberAttributedWith_GenericOnMethodSelection_KeepsReturningReachable()
    {
        // The MethodSelection overload returns a MethodSelection, so `.Returning` chains off the sugar —
        // this would not compile against the MemberSelection overload alone.
        Checker.Sentence(arch => arch.Types.Methods.AttributedWith<SugarAttribute>()
                .Returning(typeof(Task))
                .MustBePublic())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Methods.AttributedWith(typeof(SugarAttribute))
                .Returning(typeof(Task))
                .MustBePublic()));
    }

    [Fact]
    public void MemberAttributedWith_GenericOnPropertySelection_KeepsMustBeGetOnlyReachable()
    {
        // The PropertySelection overload returns a PropertySelection, so the properties-only verb chains off
        // the sugar — this would not compile against the MemberSelection overload alone.
        Checker.Sentence(arch => arch.Types.Properties.AttributedWith<SugarAttribute>()
                .MustBeGetOnly())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Properties.AttributedWith(typeof(SugarAttribute))
                .MustBeGetOnly()));
    }

    [Fact]
    public void MemberAttributedWith_GenericOnFieldSelection_KeepsMustBeReadonlyReachable()
    {
        // The FieldSelection twin of the row above — the fields-only verb after the sugar.
        Checker.Sentence(arch => arch.Types.Fields.AttributedWith<SugarAttribute>()
                .MustBeReadonly())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Fields.AttributedWith(typeof(SugarAttribute))
                .MustBeReadonly()));
    }

    [Fact]
    public void MemberAttributedWith_GenericOnMembers_ServesTheKindOnlyReceiver()
    {
        // .Members is typed MemberSelection — its KindMemberSelection is internal, so no per-kind overload
        // can name it and the MemberSelection overload is the only one that binds. That is what the fourth
        // member of the completeness set is for, and nothing else in the suite reaches it.
        Checker.Sentence(arch => arch.Types.Members.AttributedWith<SugarAttribute>()
                .MustBePublic())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Members.AttributedWith(typeof(SugarAttribute))
                .MustBePublic()));
    }

    [Fact]
    public void MemberAttributedWith_GenericOnEvents_ServesTheKindOnlyReceiver()
    {
        // .Events is the other MemberSelection-typed projection — same overload, a different kind filter
        // riding underneath it.
        Checker.Sentence(arch => arch.Types.Events.AttributedWith<SugarAttribute>()
                .MustBePublic())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Events.AttributedWith(typeof(SugarAttribute))
                .MustBePublic()));
    }

    [Fact]
    public void MemberMustBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.Methods.MustBeAttributedWith<SugarAttribute>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Methods.MustBeAttributedWith(typeof(SugarAttribute))));
    }

    [Fact]
    public void MemberMustNotBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.Sentence(arch => arch.Types.Methods.MustNotBeAttributedWith<SugarAttribute>())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Methods.MustNotBeAttributedWith(typeof(SugarAttribute))));
    }

    [Fact]
    public void AnyOf_MultiTypeSugar_ReifiesIdenticallyToWrappedSelections()
    {
        // arch.AnyOf(typeof(A), typeof(B)) ≡ arch.AnyOf(arch.Type(a), arch.Type(b)) — the multi-type noun
        // that arch.Types(params Type[]) cannot be (CS0102 against the arch.Types property).
        Checker.Sentence(arch => arch.AnyOf(typeof(SugarType), typeof(SugarBase))
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.AnyOf(arch.Type<SugarType>(), arch.Type<SugarBase>())
                .MustBeSealed()));
    }

    [Fact]
    public void Except_TypeSugar_ReifiesIdenticallyToWrappedSelections()
    {
        // .Except(typeof(A)) ≡ .Except(arch.Type(a)); several types are the union arch.AnyOf would mint, so
        // the multi-type form is the AnyOf spelling written for the caller.
        Checker.Sentence(arch => arch.Types.Except(typeof(SugarType))
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Types.Except(arch.Type(typeof(SugarType)))
                .MustBeSealed()));
        Checker.Sentence(arch => arch.Types.Except(typeof(SugarType), typeof(SugarBase))
                .MustBeSealed())
            .ShouldBe(Checker.Sentence(arch => arch.Types
                .Except(arch.AnyOf(typeof(SugarType), typeof(SugarBase)))
                .MustBeSealed()));
    }

    // Local reification markers — the sugar-equality tests only need a non-generic interface, base class,
    // and attribute; the checker never runs here.
    private interface ISugarPort;

    private abstract class SugarBase;

    private sealed class SugarAttribute : Attribute;

    private sealed class SugarType;
}

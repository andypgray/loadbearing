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
        Sentence(arch => arch.Type<SugarType>().MustBeSealed())
            .ShouldBe(Sentence(arch => arch.Type(typeof(SugarType)).MustBeSealed()));
    }

    [Fact]
    public void Implementing_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.Implementing<ISugarPort>().MustBeSealed())
            .ShouldBe(Sentence(arch => arch.Types.Implementing(typeof(ISugarPort)).MustBeSealed()));
    }

    [Fact]
    public void DerivedFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.DerivedFrom<SugarBase>().MustBeSealed())
            .ShouldBe(Sentence(arch => arch.Types.DerivedFrom(typeof(SugarBase)).MustBeSealed()));
    }

    [Fact]
    public void AttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.AttributedWith<SugarAttribute>().MustBeSealed())
            .ShouldBe(Sentence(arch => arch.Types.AttributedWith(typeof(SugarAttribute)).MustBeSealed()));
    }

    [Fact]
    public void MustImplement_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.MustImplement<ISugarPort>())
            .ShouldBe(Sentence(arch => arch.Types.MustImplement(typeof(ISugarPort))));
    }

    [Fact]
    public void MustDeriveFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.MustDeriveFrom<SugarBase>())
            .ShouldBe(Sentence(arch => arch.Types.MustDeriveFrom(typeof(SugarBase))));
    }

    [Fact]
    public void MustBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Sentence(arch => arch.Types.MustBeAttributedWith<SugarAttribute>())
            .ShouldBe(Sentence(arch => arch.Types.MustBeAttributedWith(typeof(SugarAttribute))));
    }

    private static string Sentence(Func<Arch, Constraint> constraint)
    {
        return ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule("area/rule").Enforce(constraint(arch)).Because("b")))
            .Rules.Single().Sentence;
    }

    // Local reification markers — the sugar-equality tests only need a non-generic interface, base class,
    // and attribute; the checker never runs here.
    private interface ISugarPort;

    private abstract class SugarBase;

    private sealed class SugarAttribute : Attribute;

    private sealed class SugarType;
}
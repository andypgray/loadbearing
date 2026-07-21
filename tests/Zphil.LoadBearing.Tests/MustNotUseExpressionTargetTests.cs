using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The static-form <c>MustNotUse</c> verb sugar (GRAMMAR §3.3/§4.5):
///     <c>subject.MustNotUse(() =&gt; Type.M)</c> desugars target-by-target through
///     <c>arch.Member(() =&gt; …)</c> to the identical <see cref="Member" /> leaf — so it reifies
///     identically to both the explicit <c>arch.Member</c> expression spelling and the
///     <c>typeof</c> + <c>nameof</c> spelling. The model is the sole source of prose, so an identical
///     rendered sentence proves identical reification (the <c>GenericSugarTests</c> idiom). Type/lambda
///     shapes are compile-time, so a <c>[Fact]</c> per family suffices; fact 3's mere compile proves the
///     <c>Func</c>-wins overload betterness.
/// </summary>
public class MustNotUseExpressionTargetTests
{
    [Fact]
    public void MustNotUse_VerbStaticProperty_ReifiesIdenticallyToArchMemberAndTypeof()
    {
        string verb = Sentence(arch => arch.Types.MustNotUse(() => DateTime.Now));
        string expression = Sentence(arch => arch.Types.MustNotUse(arch.Member(() => DateTime.Now)));
        string typeofForm = Sentence(arch => arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))));

        verb.ShouldBe(expression);
        verb.ShouldBe(typeofForm);
    }

    [Fact]
    public void MustNotUse_VerbStaticVoidMethod_ReifiesIdenticallyToArchMemberAndTypeof()
    {
        // The Action form: a void static method lambda binds the Expression<Action> overload.
        string verb = Sentence(arch => arch.Types.MustNotUse(() => GC.Collect()));
        string expression = Sentence(arch => arch.Types.MustNotUse(arch.Member(() => GC.Collect())));
        string typeofForm = Sentence(arch => arch.Types.MustNotUse(arch.Member(typeof(GC), nameof(GC.Collect))));

        verb.ShouldBe(expression);
        verb.ShouldBe(typeofForm);
    }

    [Fact]
    public void MustNotUse_VerbValueReturningMethod_BindsFuncForm()
    {
        // () => Guid.NewGuid() converts to BOTH new overloads (a value-returning call is a valid statement
        // expression too, so it reaches Expression<Action> as well as Expression<Func<object?>>). C# "better
        // conversion from expression" picks the Func form — the same betterness the four Arch.Member overloads
        // already rely on — so this compiles unambiguously; the compile itself is the disambiguation proof.
        // The sentence pin confirms the resolved method leaf matches the typeof spelling.
        string verb = Sentence(arch => arch.Types.MustNotUse(() => Guid.NewGuid()));
        string typeofForm = Sentence(arch => arch.Types.MustNotUse(arch.Member(typeof(Guid), nameof(Guid.NewGuid))));

        verb.ShouldBe(typeofForm);
    }

    [Fact]
    public void MustNotUse_VerbMultipleStaticTargets_ReifiesIdenticallyToArchMemberList()
    {
        // The adoption shape: the whole all-static list passed bare to the verb equals the two-arch.Member spelling.
        string verb = Sentence(arch => arch.Types.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow));
        string expression = Sentence(arch => arch.Types.MustNotUse(
            arch.Member(() => DateTime.Now),
            arch.Member(() => DateTime.UtcNow)));

        verb.ShouldBe(expression);
    }

    private static string Sentence(Func<Arch, Constraint> constraint)
    {
        return ArchModelBuilder.Build(new InlineSpec(arch => arch.Rule("area/rule").Enforce(constraint(arch)).Because("b")))
            .Rules.Single().Sentence;
    }
}
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Tests.Stubs;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     One authored sentence per modal verb the canonical sample does not exercise (the
///     vocabulary coverage pins). Constraints are rendered directly through the internal
///     renderer. The <see cref="Arch" /> is a fresh throwaway — these pins render fragments, they
///     do not build a full model.
/// </summary>
public class VocabularyFragmentTests
{
    private static readonly Arch Arch = new();

    [Fact]
    public void MustOnlyReference_StatesExternalPackagesCaveat()
    {
        // §4.1: the complement universe is solution-declared types — honesty is pinned.
        SentenceRenderer.Sentence(Arch.Types.MustOnlyReference(typeof(SqlConnection)))
            .ShouldBe("Types must reference only `SqlConnection` (external packages are not constrained by this rule).");
    }

    [Fact]
    public void MustOnlyBeReferencedBy_OmitsCaveat()
    {
        // §4.1: only solution types can be observed referencing, so no caveat is needed.
        SentenceRenderer.Sentence(Arch.Types.MustOnlyBeReferencedBy(typeof(SqlConnection)))
            .ShouldBe("Types must be referenced only by `SqlConnection`.");
    }

    [Fact]
    public void MustNotBeReferencedBy_RendersInverseVoice()
    {
        SentenceRenderer.Sentence(Arch.Types.MustNotBeReferencedBy(typeof(SqlConnection)))
            .ShouldBe("Types must not be referenced by `SqlConnection`.");
    }

    [Fact]
    public void MustNotConstruct_RendersFragment()
    {
        // The constructor-ban verb (GRAMMAR §5.3): "must not construct {list}".
        SentenceRenderer.Sentence(Arch.Types.MustNotConstruct(typeof(SqlConnection)))
            .ShouldBe("Types must not construct `SqlConnection`.");
    }

    [Fact]
    public void MustResideInNamespace_BackticksGlob()
    {
        SentenceRenderer.Sentence(Arch.Types.MustResideInNamespace("MyApp.Web.*"))
            .ShouldBe("Types must reside in `MyApp.Web.*`.");
    }

    [Fact]
    public void MustHaveNameMatching_RendersGlob()
    {
        SentenceRenderer.Sentence(Arch.Types.MustHaveNameMatching("*Repo*"))
            .ShouldBe("Types must have a name matching `*Repo*`.");
    }

    [Fact]
    public void MustImplement_BackticksType()
    {
        SentenceRenderer.Sentence(Arch.Types.MustImplement(typeof(IBillingFacade)))
            .ShouldBe("Types must implement `IBillingFacade`.");
    }

    [Fact]
    public void MustDeriveFrom_BackticksType()
    {
        SentenceRenderer.Sentence(Arch.Types.MustDeriveFrom(typeof(ControllerBase)))
            .ShouldBe("Types must derive from `ControllerBase`.");
    }

    [Fact]
    public void MustBeAttributedWith_StripsAttributeSuffixAndBrackets()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeAttributedWith(typeof(ApiControllerAttribute)))
            .ShouldBe("Types must be attributed with `[ApiController]`.");
    }

    // ---- Negative hierarchy/attribute verbs (GRAMMAR §5.3): none-of over the anchor list ----

    [Fact]
    public void MustNotImplement_SingleAnchor_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustNotImplement(typeof(IBillingFacade)))
            .ShouldBe("Types must not implement `IBillingFacade`.");
    }

    [Fact]
    public void MustNotImplement_OrList_JoinsAnchorsAndRendersOpenGenericTypeParameter()
    {
        // The or-list join (§6) plus open-generic rendering with declared type-parameter names (§5.2): two
        // anchors, the second an open generic → `IHandler<T>`.
        SentenceRenderer.Sentence(Arch.Types.MustNotImplement(typeof(IBillingFacade), typeof(IHandler<>)))
            .ShouldBe("Types must not implement `IBillingFacade` or `IHandler<T>`.");
    }

    [Fact]
    public void MustNotDeriveFrom_SingleAnchor_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustNotDeriveFrom(typeof(ControllerBase)))
            .ShouldBe("Types must not derive from `ControllerBase`.");
    }

    [Fact]
    public void MustNotBeAttributedWith_OrList_StripsAttributeSuffixBracketsAndJoins()
    {
        // Each anchor is Attribute-stripped and bracketed like the positive verb, joined as an or-list (§5.3, §6).
        SentenceRenderer.Sentence(Arch.Types.MustNotBeAttributedWith(typeof(ApiControllerAttribute), typeof(SerializableAttribute)))
            .ShouldBe("Types must not be attributed with `[ApiController]` or `[Serializable]`.");
    }

    [Fact]
    public void MustBeSealed_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeSealed()).ShouldBe("Types must be sealed.");
    }

    [Fact]
    public void MustBeStatic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeStatic()).ShouldBe("Types must be static.");
    }

    [Fact]
    public void MustBeAbstract_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeAbstract()).ShouldBe("Types must be abstract.");
    }

    [Fact]
    public void MustBePublic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBePublic()).ShouldBe("Types must be public.");
    }

    [Fact]
    public void MustBeInternal_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeInternal()).ShouldBe("Types must be internal.");
    }

    // ---- Member modal verbs (GRAMMAR §5.7): one pin per verb, all ten ----

    [Fact]
    public void Member_MustHaveSuffix_ReusesTypeSideNamingFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustHaveSuffix("Async"))
            .ShouldBe("Methods of types must be named `*Async`.");
    }

    [Fact]
    public void Member_MustHavePrefix_ReusesTypeSideNamingFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustHavePrefix("Get"))
            .ShouldBe("Methods of types must be named `Get*`.");
    }

    [Fact]
    public void Member_MustHaveNameMatching_RendersGlob()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustHaveNameMatching("*Async"))
            .ShouldBe("Methods of types must have a name matching `*Async`.");
    }

    [Fact]
    public void Member_MustBePublic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBePublic()).ShouldBe("Methods of types must be public.");
    }

    [Fact]
    public void Member_MustBeInternal_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeInternal()).ShouldBe("Methods of types must be internal.");
    }

    [Fact]
    public void Member_MustBePrivate_RendersFragment()
    {
        // Member-only vocabulary (no type-side twin).
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBePrivate()).ShouldBe("Methods of types must be private.");
    }

    [Fact]
    public void Member_MustBeStatic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeStatic()).ShouldBe("Methods of types must be static.");
    }

    [Fact]
    public void Member_MustBeAbstract_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeAbstract()).ShouldBe("Methods of types must be abstract.");
    }

    [Fact]
    public void Member_MustBeVirtual_RendersFragment()
    {
        // Member-only vocabulary (no type-side twin).
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeVirtual()).ShouldBe("Methods of types must be virtual.");
    }

    [Fact]
    public void Member_Must_RendersEscapeHatchDescription()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.Must(m => m.IsAsync, "return a Task"))
            .ShouldBe("Methods of types must return a Task.");
    }

    [Fact]
    public void Member_MustAcceptParameter_RendersArticleSafeFragment()
    {
        // The parameter-facts verb (GRAMMAR §5.7, §4.6): the article-safe "must accept a parameter of type
        // `X`" phrasing, methods-only by receiver type.
        SentenceRenderer.Sentence(Arch.Types.Methods.MustAcceptParameter(typeof(CancellationToken)))
            .ShouldBe("Methods of types must accept a parameter of type `CancellationToken`.");
    }

    [Fact]
    public void Member_MustAcceptParameter_OpenGenericRendersDeclaredTypeParameterName()
    {
        // An open-generic anchor renders declared type-parameter names — typeof(IProgress<>) → `IProgress<T>`.
        SentenceRenderer.Sentence(Arch.Types.Methods.MustAcceptParameter(typeof(IProgress<>)))
            .ShouldBe("Methods of types must accept a parameter of type `IProgress<T>`.");
    }

    // ---- Registered noun fragments + the injection-ban verb (GRAMMAR §4.7, §5.1, §5.3) ----

    [Theory]
    [InlineData(Lifetime.Singleton, "singleton-registered types")]
    [InlineData(Lifetime.Scoped, "scoped-registered types")]
    [InlineData(Lifetime.Transient, "transient-registered types")]
    public void Registered_WithLifetime_RendersLifetimePrefixedFragment(Lifetime lifetime, string expected)
    {
        // The per-lifetime noun fragment in reference position (GRAMMAR §5.1).
        SentenceRenderer.Reference(Arch.Registered(lifetime)).ShouldBe(expected);
    }

    [Fact]
    public void Registered_NoArg_RendersBareRegisteredFragment()
    {
        // The any-lifetime noun fragment (GRAMMAR §5.1).
        SentenceRenderer.Reference(Arch.Registered()).ShouldBe("registered types");
    }

    [Fact]
    public void MustNotInject_RendersFragment()
    {
        // The injection-ban verb (GRAMMAR §5.3): "must not inject {list}".
        SentenceRenderer.Sentence(Arch.Types.MustNotInject(typeof(SqlConnection)))
            .ShouldBe("Types must not inject `SqlConnection`.");
    }

    // ---- Exception-edge verbs (GRAMMAR §5.3): "must not catch {list}" and the STRICT "must throw only {list}" ----

    [Fact]
    public void MustNotCatch_RendersFragment()
    {
        // The exception-catch-ban verb (GRAMMAR §5.3): "must not catch {list}".
        SentenceRenderer.Sentence(Arch.Types.MustNotCatch(typeof(InvalidOperationException)))
            .ShouldBe("Types must not catch `InvalidOperationException`.");
    }

    [Fact]
    public void MustNotCatchUnfiltered_RendersFragment()
    {
        // The filter-aware catch-ban verb (GRAMMAR §5.3): "must not catch {list} without a `when` filter".
        // The backticks around `when` are literal output characters, not markdown the renderer adds.
        SentenceRenderer.Sentence(Arch.Types.MustNotCatchUnfiltered(typeof(Exception)))
            .ShouldBe("Types must not catch `Exception` without a `when` filter.");
    }

    [Fact]
    public void MustNotThrow_RendersFragment()
    {
        // The throw-ban verb (GRAMMAR §5.3): "must not throw {list}" — the ban polarity beside MustOnlyThrow.
        SentenceRenderer.Sentence(Arch.Types.MustNotThrow(typeof(InvalidOperationException)))
            .ShouldBe("Types must not throw `InvalidOperationException`.");
    }

    [Fact]
    public void MustOnlyThrow_RendersStrictFragmentWithNoCaveat()
    {
        // The strict throw-allowlist verb (GRAMMAR §5.3): "must throw only {list}". Unlike MustOnlyReference,
        // there is NO "(external packages…)" caveat — MustOnlyThrow constrains external thrown types too, and
        // exact string equality proves the parenthetical is absent (the strictness rendering).
        SentenceRenderer.Sentence(Arch.Types.MustOnlyThrow(typeof(InvalidOperationException)))
            .ShouldBe("Types must throw only `InvalidOperationException`.");
    }

    [Fact]
    public void MustOnlyThrow_RendersNoParentheticalCaveat()
    {
        // Belt-and-braces beside the exact-equality pin: the absence of the "(external packages are not
        // constrained by this rule)" caveat that MustOnlyReference carries IS the strictness rendering —
        // MustOnlyThrow constrains external thrown types too, so no parenthetical exemption is emitted.
        string sentence = SentenceRenderer.Sentence(Arch.Types.MustOnlyThrow(typeof(InvalidOperationException)));
        sentence.ShouldNotContain("(");
    }

    // ---- Signature-exposure verb (GRAMMAR §5.3): "must not expose {list}" ----

    [Fact]
    public void MustNotExpose_RendersFragment()
    {
        // The signature-exposure verb (GRAMMAR §5.3): "must not expose {list}".
        SentenceRenderer.Sentence(Arch.Types.MustNotExpose(typeof(SqlConnection)))
            .ShouldBe("Types must not expose `SqlConnection`.");
    }

    // ---- String attribute anchors (GRAMMAR §5.2–§5.3): the escape hatch names the attribute definition by
    //      fully-qualified string, and renders byte-identically to its typeof twin above ----

    [Fact]
    public void MustBeAttributedWith_StringAnchor_RendersTheTypeofSentence()
    {
        // Byte-for-byte the MustBeAttributedWith_StripsAttributeSuffixAndBrackets pin: the string carries the
        // Attribute suffix, and the renderer strips and brackets it exactly as it does a typeof anchor.
        SentenceRenderer.Sentence(Arch.Types.MustBeAttributedWith("Zphil.LoadBearing.Tests.Stubs.ApiControllerAttribute"))
            .ShouldBe("Types must be attributed with `[ApiController]`.");
    }

    [Fact]
    public void MustNotBeAttributedWith_StringAnchors_RenderTheTypeofOrList()
    {
        // The or-list join over a homogeneous string anchor list — the MustNotBeAttributedWith_OrList pin's
        // sentence, reached without a typeof.
        SentenceRenderer.Sentence(Arch.Types.MustNotBeAttributedWith(
                "Zphil.LoadBearing.Tests.Stubs.ApiControllerAttribute", "System.SerializableAttribute"))
            .ShouldBe("Types must not be attributed with `[ApiController]` or `[Serializable]`.");
    }

    [Fact]
    public void AttributedWith_StringAnchor_RendersTheTypeofFragment()
    {
        // The adjective arm of the same equivalence, asserted against the typeof form directly.
        SentenceRenderer.Sentence(Arch.Types.AttributedWith("Zphil.LoadBearing.Tests.Stubs.ApiControllerAttribute").MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.AttributedWith(typeof(ApiControllerAttribute)).MustBeSealed()));
    }

    // ---- String hierarchy anchors (GRAMMAR §5.2–§5.3): the same escape hatch names an interface or a base
    //      type by fully-qualified string, and renders byte-identically to its typeof twin above ----

    [Fact]
    public void MustImplement_StringAnchor_RendersTheTypeofSentence()
    {
        SentenceRenderer.Sentence(Arch.Types.MustImplement("Zphil.LoadBearing.Tests.Stubs.IBillingFacade"))
            .ShouldBe("Types must implement `IBillingFacade`.");
    }

    [Fact]
    public void MustDeriveFrom_StringAnchor_RendersTheTypeofSentence()
    {
        SentenceRenderer.Sentence(Arch.Types.MustDeriveFrom("Zphil.LoadBearing.Tests.Stubs.ControllerBase"))
            .ShouldBe("Types must derive from `ControllerBase`.");
    }

    [Fact]
    public void MustNotImplement_StringAnchors_RenderTheTypeofOrList()
    {
        // Byte-for-byte the MustNotImplement_OrList pin, reached without a typeof: the open generic's declared
        // type-parameter name is in the string, because the string is what a report prints.
        SentenceRenderer.Sentence(Arch.Types.MustNotImplement(
                "Zphil.LoadBearing.Tests.Stubs.IBillingFacade", "Zphil.LoadBearing.Tests.Stubs.IHandler<T>"))
            .ShouldBe("Types must not implement `IBillingFacade` or `IHandler<T>`.");
    }

    [Fact]
    public void MustNotDeriveFrom_StringAnchor_RendersTheTypeofSentence()
    {
        SentenceRenderer.Sentence(Arch.Types.MustNotDeriveFrom("Zphil.LoadBearing.Tests.Stubs.ControllerBase"))
            .ShouldBe("Types must not derive from `ControllerBase`.");
    }

    [Fact]
    public void Implementing_StringAnchor_RendersTheTypeofFragment()
    {
        // The adjective arms of the same equivalence, asserted against the typeof forms directly.
        SentenceRenderer.Sentence(Arch.Types.Implementing("Zphil.LoadBearing.Tests.Stubs.IHandler<T>").MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.Implementing(typeof(IHandler<>)).MustBeSealed()));
    }

    [Fact]
    public void DerivedFrom_StringAnchor_RendersTheTypeofFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.DerivedFrom("Zphil.LoadBearing.Tests.Stubs.ControllerBase").MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.DerivedFrom(typeof(ControllerBase)).MustBeSealed()));
    }

    // ---- The member attribute axis (GRAMMAR §5.7): one adjective and both verbs. The VERBS reuse the
    //      type-side phrases verbatim — verb position is unambiguous, so there is nothing to disambiguate.
    //      The ADJECTIVE does not: it premodifies the member head instead of trailing the type reference ----

    [Fact]
    public void MemberAttributedWith_PremodifiesTheMemberHead()
    {
        // "`[X]`-attributed methods of …", not "methods of types attributed with `[X]`" — the latter is what
        // the TYPE-side adjective before a projection renders, and it names a different subject.
        SentenceRenderer.Sentence(Arch.Types.Methods.AttributedWith(typeof(ApiControllerAttribute)).MustBePublic())
            .ShouldBe("`[ApiController]`-attributed methods of types must be public.");
    }

    [Fact]
    public void Member_MustBeAttributedWith_ReusesTypeSideVerbPhrase()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeAttributedWith(typeof(ApiControllerAttribute)))
            .ShouldBe("Methods of types must be attributed with `[ApiController]`.");
    }

    [Fact]
    public void Member_MustNotBeAttributedWith_ReusesTypeSideOrList()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustNotBeAttributedWith(
                typeof(ApiControllerAttribute), typeof(SerializableAttribute)))
            .ShouldBe("Methods of types must not be attributed with `[ApiController]` or `[Serializable]`.");
    }
}
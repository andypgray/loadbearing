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
    public void MustOnlyReferenceItself_StatesTheSameExternalPackagesCaveat()
    {
        // §4.1/§5.3: the leaf form's complement universe is the outbound verb's, so the caveat is too — a
        // leaf of the solution's reference graph may still take a NuGet dependency. A layer subject rather
        // than the bare Types noun every other row here uses, because "itself" refers to the selection and
        // a singular noun is the only subject that reads back as the law means it.
        SentenceRenderer.Sentence(Arch.Layer("Tracking", "MyApp.Tracking.*").MustOnlyReferenceItself())
            .ShouldBe("The Tracking layer must reference only itself (external packages are not constrained by this rule).");
    }

    [Fact]
    public void MustOnlyBeReferencedByItself_RendersTheInboundLeaf()
    {
        // §5.3: the inbound twin of the outbound leaf, and it takes no caveat for the same reason
        // MustOnlyBeReferencedBy does not — only solution types can be observed referencing (§4.1).
        SentenceRenderer.Sentence(Arch.Layer("Tracking", "MyApp.Tracking.*").MustOnlyBeReferencedByItself())
            .ShouldBe("The Tracking layer must be referenced only by itself.");
    }

    [Fact]
    public void MustNotReferenceEachOther_OverAFamily_SpeaksOfTheOthers()
    {
        // §5.1/§5.3: the cross-cell ban's targets are the subject's own cells, so the collective voice
        // needs no list — "the others" is every cell but the one the sentence is speaking of.
        Selection family = Arch.Each(
            Arch.Layer("Host", "MyApp.Host.*"), Arch.Layer("Adapter", "MyApp.Adapter.*"), Arch.Layer("Pack", "MyApp.Pack.*"));

        SentenceRenderer.Sentence(family.MustNotReferenceEachOther())
            .ShouldBe("Each of the Host, Adapter and Pack layers must not reference the others.");
    }

    [Fact]
    public void MustNotReferenceEachOther_OverAProjectFamily_SpeaksOfTheProjects()
    {
        SentenceRenderer.Sentence(Arch.Each(Arch.Projects.Matching("MyApp.*")).MustNotReferenceEachOther())
            .ShouldBe("Each of the projects matching `MyApp.*` must not reference the others.");
    }

    [Fact]
    public void MustNotHaveCircularReferences_OverAFamily_SpeaksOfTheOthers()
    {
        // §5.1/§5.3: the cycle gate's nodes are the subject's own cells, so it borrows the cross-cell ban's
        // collective phrasing — "the others" is every cell but the one the sentence is speaking of.
        Selection family = Arch.Each(
            Arch.Layer("Model", "Zphil.LoadBearing.Model.*"),
            Arch.Layer("Checking", "Zphil.LoadBearing.Checking.*"),
            Arch.Layer("Rendering", "Zphil.LoadBearing.Rendering.*"));

        SentenceRenderer.Sentence(family.MustNotHaveCircularReferences())
            .ShouldBe("Each of the Model, Checking and Rendering layers must not have circular references with the others.");
    }

    [Fact]
    public void FamilyVerbs_InTypesVoice_NameTheCellWord()
    {
        // §5.3/§6: the four family-aware phrases are voice-aware, and only on a family — an adjective
        // switches the subject to types voice, where "the others" and "itself" have to say what a cell is.
        Selection layers = Arch.Each(Arch.Layer("Dispatch", "Ops.Dispatch.*"), Arch.Layer("Tracking", "Ops.Tracking.*"))
            .WithSuffix("Engine");
        Selection projects = Arch.Each(Arch.Projects.Matching("Nop.Plugin.*"))
            .Authored();

        SentenceRenderer.Sentence(layers.MustNotReferenceEachOther())
            .ShouldBe("Types in each of the Dispatch and Tracking layers named `*Engine` must not reference the other layers.");
        SentenceRenderer.Sentence(layers.MustNotHaveCircularReferences())
            .ShouldBe(
                "Types in each of the Dispatch and Tracking layers named `*Engine` must not have circular references "
                + "with the other layers.");
        SentenceRenderer.Sentence(layers.MustOnlyBeReferencedByItself())
            .ShouldBe("Types in each of the Dispatch and Tracking layers named `*Engine` must be referenced only by their own layer.");
        SentenceRenderer.Sentence(layers.MustOnlyReferenceItself())
            .ShouldBe(
                "Types in each of the Dispatch and Tracking layers named `*Engine` must reference only their own layer "
                + "(external packages are not constrained by this rule).");
        SentenceRenderer.Sentence(projects.MustNotReferenceEachOther())
            .ShouldBe("Authored types in each of the projects matching `Nop.Plugin.*` must not reference the other projects.");
        SentenceRenderer.Sentence(projects.MustOnlyReferenceItself())
            .ShouldBe(
                "Authored types in each of the projects matching `Nop.Plugin.*` must reference only their own project "
                + "(external packages are not constrained by this rule).");
    }

    [Fact]
    public void TheTwoLeafVerbs_OnAPlainSubject_AgreeInNumberWithTheTypesHead()
    {
        // §5.3/§6: a plain subject in types voice is plural — its head is "types" — so the reflexive is
        // "themselves"; only the collective voice (a bare layer, above) says "itself", and only a family
        // names its cell instead. A union is types voice too, and has no cell.
        Selection refined = Arch.Namespace("MyApp.Tracking.*")
            .WithSuffix("Service");
        Selection union = Arch.AnyOf(Arch.Namespace("MyApp.Tracking.*"), Arch.Namespace("MyApp.Billing.*"));

        SentenceRenderer.Sentence(refined.MustOnlyReferenceItself())
            .ShouldBe(
                "Types in `MyApp.Tracking.*` named `*Service` must reference only themselves "
                + "(external packages are not constrained by this rule).");
        SentenceRenderer.Sentence(refined.MustOnlyBeReferencedByItself())
            .ShouldBe("Types in `MyApp.Tracking.*` named `*Service` must be referenced only by themselves.");
        SentenceRenderer.Sentence(union.MustOnlyBeReferencedByItself())
            .ShouldBe("Types in `MyApp.Tracking.*` or `MyApp.Billing.*` must be referenced only by themselves.");
    }

    [Fact]
    public void FamilyOfThreeModules_RendersTheOperationsSentence()
    {
        // The corpus witness (GRAMMAR §5.1): one sentence where the spec used to state three, with the
        // three Contracts cones carved out of the subject and still counting as each module's own.
        Selection modules = Arch.Each(
                Arch.Layer("Dispatch", "Meridian.Operations.Dispatch.*"),
                Arch.Layer("Tracking", "Meridian.Operations.Tracking.*"),
                Arch.Layer("Invoicing", "Meridian.Operations.Invoicing.*"))
            .Except(
                Arch.Namespace("Meridian.Operations.Dispatch.Contracts.*"),
                Arch.Namespace("Meridian.Operations.Tracking.Contracts.*"),
                Arch.Namespace("Meridian.Operations.Invoicing.Contracts.*"));

        SentenceRenderer.Sentence(modules.MustOnlyBeReferencedByItself())
            .ShouldBe(
                "Types in each of the Dispatch, Tracking and Invoicing layers, except types in "
                + "`Meridian.Operations.Dispatch.Contracts.*`, `Meridian.Operations.Tracking.Contracts.*` or "
                + "`Meridian.Operations.Invoicing.Contracts.*`, must be referenced only by their own layer.");
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
        SentenceRenderer.Sentence(Arch.Types.MustBeSealed())
            .ShouldBe("Types must be sealed.");
    }

    [Fact]
    public void MustBeStatic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeStatic())
            .ShouldBe("Types must be static.");
    }

    [Fact]
    public void MustBeAbstract_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeAbstract())
            .ShouldBe("Types must be abstract.");
    }

    [Fact]
    public void MustBePublic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBePublic())
            .ShouldBe("Types must be public.");
    }

    [Fact]
    public void MustBeInternal_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.MustBeInternal())
            .ShouldBe("Types must be internal.");
    }

    // ---- Member modal verbs (GRAMMAR §5.7): one pin per verb ----

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
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBePublic())
            .ShouldBe("Methods of types must be public.");
    }

    [Fact]
    public void Member_MustBeInternal_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeInternal())
            .ShouldBe("Methods of types must be internal.");
    }

    [Fact]
    public void Member_MustBePrivate_RendersFragment()
    {
        // Member-only vocabulary (no type-side twin).
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBePrivate())
            .ShouldBe("Methods of types must be private.");
    }

    [Fact]
    public void Member_MustBeStatic_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeStatic())
            .ShouldBe("Methods of types must be static.");
    }

    [Fact]
    public void Member_MustBeAbstract_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeAbstract())
            .ShouldBe("Methods of types must be abstract.");
    }

    [Fact]
    public void Member_MustBeVirtual_RendersFragment()
    {
        // Member-only vocabulary (no type-side twin).
        SentenceRenderer.Sentence(Arch.Types.Methods.MustBeVirtual())
            .ShouldBe("Methods of types must be virtual.");
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

    [Fact]
    public void Member_MustBeGetOnly_RendersFragment()
    {
        // Properties-only by receiver type (GRAMMAR §5.7) — the fragment is the hyphenated adjective, not a
        // clause about setters, so the sentence stays as short as the verbs beside it.
        SentenceRenderer.Sentence(Arch.Types.Properties.MustBeGetOnly())
            .ShouldBe("Properties of types must be get-only.");
    }

    [Fact]
    public void Member_MustBeReadonly_RendersFragment()
    {
        // Fields-only by receiver type (GRAMMAR §5.7). The fragment spells the C# keyword, which is why the
        // verb is MustBeReadonly while the fact it reads is IMemberInfo.IsReadOnly.
        SentenceRenderer.Sentence(Arch.Types.Fields.MustBeReadonly())
            .ShouldBe("Fields of types must be readonly.");
    }

    // ---- Member shape adjective (GRAMMAR §5.7, §6): a head premodifier, like the member attribute one ----

    [Fact]
    public void Member_ThatAreStatic_PremodifiesTheKindPlural()
    {
        SentenceRenderer.Sentence(Arch.Types.Fields.ThatAreStatic()
                .MustBeReadonly())
            .ShouldBe("Static fields of types must be readonly.");
    }

    // ---- Registered noun fragments + the injection-ban verb (GRAMMAR §4.7, §5.1, §5.3) ----

    [Theory]
    [InlineData(Lifetime.Singleton, "singleton-registered types")]
    [InlineData(Lifetime.Scoped, "scoped-registered types")]
    [InlineData(Lifetime.Transient, "transient-registered types")]
    public void Registered_WithLifetime_RendersLifetimePrefixedFragment(Lifetime lifetime, string expected)
    {
        // The per-lifetime noun fragment in reference position (GRAMMAR §5.1).
        SentenceRenderer.Reference(Arch.Registered(lifetime))
            .ShouldBe(expected);
    }

    [Fact]
    public void Registered_NoArg_RendersBareRegisteredFragment()
    {
        // The any-lifetime noun fragment (GRAMMAR §5.1).
        SentenceRenderer.Reference(Arch.Registered())
            .ShouldBe("registered types");
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
    public void MustNotSwallow_RendersFragment()
    {
        // The rethrow-aware catch-ban verb (GRAMMAR §5.3): "must not swallow {list}". A single verb word rather
        // than a participle on `catch`, because the fact it reads is a composite of three (§10).
        SentenceRenderer.Sentence(Arch.Types.MustNotSwallow(typeof(Exception)))
            .ShouldBe("Types must not swallow `Exception`.");
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
        SentenceRenderer.Sentence(Arch.Types.AttributedWith("Zphil.LoadBearing.Tests.Stubs.ApiControllerAttribute")
                .MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.AttributedWith(typeof(ApiControllerAttribute))
                .MustBeSealed()));
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
        SentenceRenderer.Sentence(Arch.Types.Implementing("Zphil.LoadBearing.Tests.Stubs.IHandler<T>")
                .MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.Implementing(typeof(IHandler<>))
                .MustBeSealed()));
    }

    [Fact]
    public void DerivedFrom_StringAnchor_RendersTheTypeofFragment()
    {
        SentenceRenderer.Sentence(Arch.Types.DerivedFrom("Zphil.LoadBearing.Tests.Stubs.ControllerBase")
                .MustBeSealed())
            .ShouldBe(SentenceRenderer.Sentence(Arch.Types.DerivedFrom(typeof(ControllerBase))
                .MustBeSealed()));
    }

    // ---- The member attribute axis (GRAMMAR §5.7): one adjective and both verbs. The VERBS reuse the
    //      type-side phrases verbatim — verb position is unambiguous, so there is nothing to disambiguate.
    //      The ADJECTIVE does not: it premodifies the member head instead of trailing the type reference ----

    [Fact]
    public void MemberAttributedWith_PremodifiesTheMemberHead()
    {
        // "`[X]`-attributed methods of …", not "methods of types attributed with `[X]`" — the latter is what
        // the TYPE-side adjective before a projection renders, and it names a different subject.
        SentenceRenderer.Sentence(Arch.Types.Methods.AttributedWith(typeof(ApiControllerAttribute))
                .MustBePublic())
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

    // ---- Membership and coverage verbs (GRAMMAR §5.3, §4.1, §4.7): where a type must live, and that it
    //      must be registered at all ----

    [Fact]
    public void MustResideInProject_BackticksProjectName()
    {
        // The project-residence verb (GRAMMAR §5.3): "must reside in project `{name}`". The project noun's
        // "project" word rides in the verb phrase, so the sentence names the axis rather than a bare glob.
        SentenceRenderer.Sentence(Arch.Types.MustResideInProject("MyApp.Web"))
            .ShouldBe("Types must reside in project `MyApp.Web`.");
    }

    [Fact]
    public void MustBelongTo_OrJoinsTheMembershipsInReferencePosition()
    {
        // The coverage verb (GRAMMAR §5.3, §10): "must belong to {list}". The memberships render in
        // reference position and or-join, so the any-of reading is stated by the sentence itself rather
        // than left to a reader's assumption about how a list of memberships is quantified.
        SentenceRenderer.Sentence(Arch.Types.MustBelongTo(
                Arch.Layer("Domain", "MyApp.Domain.*"), Arch.Layer("Web", "MyApp.Web.*")))
            .ShouldBe("Types must belong to the Domain layer or the Web layer.");
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_BackticksTemplateUnsubstituted()
    {
        // The correspondence verb (GRAMMAR §5.3, §10): the template renders backticked and unsubstituted,
        // exactly as MustHaveSuffix renders `*Handler` — the sentence states the law, and the law is the
        // pattern. The among selection renders in reference position, head-substituted to "interfaces".
        SentenceRenderer.Sentence(Arch.Types.MustHaveExactlyOneCounterpart(
                among: Arch.Types.OfKind(TypeKind.Interface), named: "I{Name}"))
            .ShouldBe("Types must have exactly one counterpart named `I{Name}` among interfaces.");
    }

    [Fact]
    public void MustBeRegistered_RendersFragment()
    {
        // The registration-completeness verb (GRAMMAR §5.3, §4.7): nullary, because its membership is a
        // fact read from the container registrations rather than anything the sentence authors.
        SentenceRenderer.Sentence(Arch.Types.MustBeRegistered())
            .ShouldBe("Types must be registered.");
    }

    // ---- The packaging verbs over a project subject (GRAMMAR §4.10): the artifact axis, whose subject is
    //      the build output rather than a set of types ----

    [Fact]
    public void MustOnlyTarget_BackticksTheFrameworkAndCarriesNoCaveat()
    {
        // STRICT, and the caveat's ABSENCE is the strictness rendering — as with MustOnlyThrow. A project's
        // target frameworks are a closed set, so there is nothing this rule declines to constrain.
        SentenceRenderer.Sentence(Arch.Projects.Named("Zphil.LoadBearing").MustOnlyTarget("netstandard2.0"))
            .ShouldBe("Project `Zphil.LoadBearing` must target only `netstandard2.0`.");
    }

    [Fact]
    public void MustReferenceNoPackages_StatesTheDeclaredReferencesCaveat()
    {
        // The honesty boundary, pinned like MustOnlyReference's: the model holds what a project declares,
        // so the sentence says the transitive graph is not seen rather than letting a reader assume it is.
        SentenceRenderer.Sentence(Arch.Projects.Named("Zphil.LoadBearing").MustReferenceNoPackages())
            .ShouldBe(
                "Project `Zphil.LoadBearing` must reference no NuGet packages "
                + "(declared references only; transitive dependencies are not seen).");
    }

    [Fact]
    public void MustLockPackages_PremodifiedSubjectHead_RendersFragment()
    {
        // `.Packable()` premodifies the bare plural head, exactly as `.Authored()` does on the type side.
        SentenceRenderer.Sentence(Arch.Projects.Packable().MustLockPackages())
            .ShouldBe("Packable projects must lock package restore.");
    }

    [Fact]
    public void MustNotBePackable_InlineGlobAdjective_RendersFragment()
    {
        SentenceRenderer.Sentence(Arch.Projects.Matching("Zphil.*").MustNotBePackable())
            .ShouldBe("Projects matching `Zphil.*` must not be packable.");
    }
}

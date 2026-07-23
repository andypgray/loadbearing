using Shouldly;
using Xunit;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Tests.Stubs;
using Zphil.LoadBearing.Tests.Stubs.Billing;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The §6 assembly mechanics: the collective-vs-types voice switch, subject heads, the five
///     <c>OfKind</c> plurals, sentence-final canonicalization of <c>Except</c>/<c>Where</c>,
///     colliding-simple-name qualification, list joins, and generic rendering.
/// </summary>
public class SentenceAssemblyTests
{
    private static readonly Arch Arch = new();

    [Fact]
    public void BareLayerSubject_SpeaksCollectively()
    {
        SentenceRenderer.Subject(Arch.Layer("Domain", "MyApp.Domain.*")).ShouldBe("The Domain layer");
    }

    [Fact]
    public void LayerSubjectWithAnyAdjective_SwitchesToTypesVoice()
    {
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Subject(web.WithSuffix("Controller")).ShouldBe("Types in the Web layer named `*Controller`");
    }

    [Fact]
    public void BareTypesSubject_IsCapitalizedHead()
    {
        SentenceRenderer.Subject(Arch.Types).ShouldBe("Types");
    }

    [Fact]
    public void NamespaceNounSubject_RendersLocative()
    {
        SentenceRenderer.Subject(Arch.Namespace("MyApp.*")).ShouldBe("Types in `MyApp.*`");
    }

    [Fact]
    public void ProjectNounSubject_RendersProjectLocative()
    {
        SentenceRenderer.Subject(Arch.Project("MyApp.Web")).ShouldBe("Types in project `MyApp.Web`");
    }

    [Theory]
    [InlineData(TypeKind.Class, "Classes")]
    [InlineData(TypeKind.Interface, "Interfaces")]
    [InlineData(TypeKind.Struct, "Structs")]
    [InlineData(TypeKind.Enum, "Enums")]
    [InlineData(TypeKind.Delegate, "Delegates")]
    public void OfKind_SubstitutesTheHeadPlural(TypeKind kind, string expectedHead)
    {
        SentenceRenderer.Subject(Arch.Types.OfKind(kind)).ShouldBe(expectedHead);
    }

    [Fact]
    public void WithPrefixSubjectHead_RendersNamedGlob()
    {
        SentenceRenderer.Subject(Arch.Types.WithPrefix("Legacy")).ShouldBe("Types named `Legacy*`");
    }

    [Fact]
    public void WithNameMatchingSubjectHead_RendersMatchesClause()
    {
        SentenceRenderer.Subject(Arch.Types.WithNameMatching("*Repo*")).ShouldBe("Types whose name matches `*Repo*`");
    }

    [Fact]
    public void DerivedFromSubjectHead_RendersDerivedClause()
    {
        SentenceRenderer.Subject(Arch.Types.DerivedFrom(typeof(ControllerBase))).ShouldBe("Types derived from `ControllerBase`");
    }

    [Fact]
    public void AttributedWithSubjectHead_StripsAttributeAndBrackets()
    {
        SentenceRenderer.Subject(Arch.Types.AttributedWith(typeof(ApiControllerAttribute)))
            .ShouldBe("Types attributed with `[ApiController]`");
    }

    [Fact]
    public void Except_CanonicalizesToSentenceFinal_RegardlessOfChainPosition()
    {
        Selection exclusion = Arch.Type(typeof(SqlConnection));
        string chainedBefore = SentenceRenderer.Subject(Arch.Types.Except(exclusion).InNamespace("MyApp.*"));
        string chainedAfter = SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").Except(exclusion));

        chainedBefore.ShouldBe("Types in `MyApp.*`, except `SqlConnection`");
        chainedAfter.ShouldBe(chainedBefore);
    }

    [Fact]
    public void Where_RendersDescriptionAsSentenceFinalRelativeClause()
    {
        Selection selection = Arch.Types.InNamespace("MyApp.*")
            .Where(t => t.Name.Any(char.IsDigit), "whose name contains a digit");
        SentenceRenderer.Subject(selection).ShouldBe("Types in `MyApp.*` whose name contains a digit");
    }

    [Fact]
    public void CollidingSimpleNames_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotReference(typeof(Order), typeof(Stubs.Sales.Order));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not reference `Billing.Order` or `Sales.Order`.");
    }

    [Fact]
    public void TwoTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotReference(typeof(SqlConnection), typeof(ControllerBase));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not reference `SqlConnection` or `ControllerBase`.");
    }

    [Fact]
    public void ThreeTargets_JoinWithCommasAndOr_NoOxfordComma()
    {
        Constraint constraint = Arch.Types.MustNotReference(
            typeof(SqlConnection), typeof(ControllerBase), typeof(IBillingFacade));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not reference `SqlConnection`, `ControllerBase` or `IBillingFacade`.");
    }

    [Fact]
    public void OpenGenericWithTwoParameters_RendersDeclaredParameterNames()
    {
        SentenceRenderer.Subject(Arch.Types.Implementing(typeof(IDictionary<,>)))
            .ShouldBe("Types implementing `IDictionary<TKey, TValue>`");
    }

    [Fact]
    public void MustNotUse_TwoMembersOfSameType_RendersMemberList()
    {
        Constraint constraint = Arch.Types.MustNotUse(
            Arch.Member(typeof(DateTime), nameof(DateTime.Now)),
            Arch.Member(typeof(DateTime), nameof(DateTime.UtcNow)));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not use `DateTime.Now` or `DateTime.UtcNow`.");
    }

    [Fact]
    public void MustNotUse_BareLayerSubject_SpeaksCollectively()
    {
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustNotUse(Arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .ShouldBe("The Web layer must not use `DateTime.Now`.");
    }

    [Fact]
    public void MustNotUse_MethodMember_AppendsParens()
    {
        Constraint constraint = Arch.Types.MustNotUse(Arch.Member(typeof(Task), nameof(Task.Wait)));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not use `Task.Wait()`.");
    }

    [Fact]
    public void MustNotUse_GenericAnchorProperty_RendersDeclaredTypeParameterName()
    {
        Constraint constraint = Arch.Types.MustNotUse(Arch.Member(typeof(Task<>), "Result"));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not use `Task<TResult>.Result`.");
    }

    [Fact]
    public void MustNotUse_CollidingDeclaringTypes_SameMemberName_WidenWithTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotUse(
            Arch.Member(typeof(Order), nameof(Order.Total)),
            Arch.Member(typeof(Stubs.Sales.Order), nameof(Stubs.Sales.Order.Total)));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not use `Billing.Order.Total` or `Sales.Order.Total`.");
    }

    [Fact]
    public void MustNotUse_CollidingDeclaringTypes_DifferentMemberNames_StillWiden()
    {
        Constraint constraint = Arch.Types.MustNotUse(
            Arch.Member(typeof(Order), nameof(Order.Total)),
            Arch.Member(typeof(Stubs.Sales.Order), nameof(Stubs.Sales.Order.Refresh)));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not use `Billing.Order.Total` or `Sales.Order.Refresh()`.");
    }

    // ---- MustNotConstruct (GRAMMAR §5.3, §3.3): dependency-shape verb over selection/type targets ----

    [Fact]
    public void MustNotConstruct_SelectionTarget_RendersConstructList()
    {
        // The Selection overload: a pattern-selection target renders in reference position.
        Constraint constraint = Arch.Types.MustNotConstruct(Arch.Namespace("MyApp.Services.*"));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not construct types in `MyApp.Services.*`.");
    }

    [Fact]
    public void MustNotConstruct_TypeSugar_BackticksSimpleName()
    {
        // The Type sugar overload wraps the bare type as a single-type selection (arch.Type written for you).
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(SqlConnection));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not construct `SqlConnection`.");
    }

    [Fact]
    public void MustNotConstruct_MultipleTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(SqlConnection), typeof(ControllerBase));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not construct `SqlConnection` or `ControllerBase`.");
    }

    [Fact]
    public void MustNotConstruct_CollidingSimpleNames_QualifyWithMinimalTrailingSegments()
    {
        // Shares TargetList with the reference verbs, so colliding simple names widen identically.
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(Order), typeof(Stubs.Sales.Order));
        SentenceRenderer.Sentence(constraint).ShouldBe("Types must not construct `Billing.Order` or `Sales.Order`.");
    }

    // ---- Colliding anchors, negative hierarchy/attribute verbs (GRAMMAR §6): the raw-Type anchor lists
    //      widen by the same minimal-trailing-segments rule as the dependency target lists ----

    [Fact]
    public void MustNotImplement_CollidingAnchors_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotImplement(typeof(IReceipt), typeof(Stubs.Sales.IReceipt));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not implement `Billing.IReceipt` or `Sales.IReceipt`.");
    }

    [Fact]
    public void MustNotDeriveFrom_CollidingAnchors_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotDeriveFrom(typeof(LedgerBase), typeof(Stubs.Sales.LedgerBase));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not derive from `Billing.LedgerBase` or `Sales.LedgerBase`.");
    }

    [Fact]
    public void MustNotBeAttributedWith_CollidingAnchors_WidenInsideTheBrackets()
    {
        // The attribute form qualifies inside the brackets — `[Billing.Audit]` / `[Sales.Audit]`, not a bare `[Audit]`.
        Constraint constraint = Arch.Types.MustNotBeAttributedWith(typeof(AuditAttribute), typeof(Stubs.Sales.AuditAttribute));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not be attributed with `[Billing.Audit]` or `[Sales.Audit]`.");
    }

    // ---- Exception edges (GRAMMAR §5.3, §3.3): the catch-ban and STRICT throw-allowlist dependency verbs ----

    [Fact]
    public void MustNotCatch_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6): a bare layer subject speaks collectively — "The Web layer must not catch …".
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustNotCatch(typeof(InvalidOperationException)))
            .ShouldBe("The Web layer must not catch `InvalidOperationException`.");
    }

    [Fact]
    public void MustNotCatch_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        // Head truth under adjectives (§6): a WithSuffix-bearing layer subject switches to types voice —
        // "Types in the Web layer named `*Controller` …", never a bare "The Web layer …".
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.WithSuffix("Controller").MustNotCatch(typeof(Exception)))
            .ShouldBe("Types in the Web layer named `*Controller` must not catch `Exception`.");
    }

    [Fact]
    public void MustNotCatch_MultipleTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotCatch(typeof(InvalidOperationException), typeof(TimeoutException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not catch `InvalidOperationException` or `TimeoutException`.");
    }

    [Fact]
    public void MustNotCatch_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        // Catch targets share TargetList, so colliding exception names widen like the reference verbs.
        Constraint constraint = Arch.Types.MustNotCatch(typeof(DataException), typeof(Stubs.Sales.DataException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not catch `Billing.DataException` or `Sales.DataException`.");
    }

    [Fact]
    public void MustOnlyThrow_NamespaceSubject_RendersLocativeAndStrictAllowlist()
    {
        // The namespace-locative subject + the strict throw allowlist: exact equality proves no external-
        // packages caveat rides along (unlike MustOnlyReference), which is the strictness rendering (§5.3).
        Constraint constraint = Arch.Namespace("MyApp.Domain.*").MustOnlyThrow(typeof(InvalidOperationException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types in `MyApp.Domain.*` must throw only `InvalidOperationException`.");
    }

    [Fact]
    public void MustOnlyThrow_ThreeTargets_JoinWithCommasAndOr_NoOxfordComma()
    {
        // Shares TargetList with the reference verbs: three targets join "`A`, `B` or `C`" with no Oxford comma.
        Constraint constraint = Arch.Types.MustOnlyThrow(
            typeof(InvalidOperationException), typeof(ArgumentException), typeof(TimeoutException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must throw only `InvalidOperationException`, `ArgumentException` or `TimeoutException`.");
    }

    [Fact]
    public void MustOnlyThrow_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustOnlyThrow(typeof(DataException), typeof(Stubs.Sales.DataException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must throw only `Billing.DataException` or `Sales.DataException`.");
    }

    // ---- Signature exposure (GRAMMAR §5.3, §3.3): the dependency-shape exposure-ban verb ----

    [Fact]
    public void MustNotExpose_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6): a bare layer subject speaks collectively — "The Web layer must not expose …".
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustNotExpose(typeof(SqlConnection)))
            .ShouldBe("The Web layer must not expose `SqlConnection`.");
    }

    [Fact]
    public void MustNotExpose_MultipleTargets_JoinWithOr()
    {
        // Shares TargetList with the other dependency verbs, so multiple targets join "`A` or `B`".
        Constraint constraint = Arch.Types.MustNotExpose(typeof(SqlConnection), typeof(ControllerBase));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not expose `SqlConnection` or `ControllerBase`.");
    }

    [Fact]
    public void MustNotExpose_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        // Reuses the Order collision pair; expose shares TargetList so its targets widen identically.
        Constraint constraint = Arch.Types.MustNotExpose(typeof(Order), typeof(Stubs.Sales.Order));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not expose `Billing.Order` or `Sales.Order`.");
    }

    // ---- Member subjects (GRAMMAR §4.6, §5.7, §6) ----

    [Fact]
    public void MemberProjections_RenderKindPluralHeads()
    {
        // The five projection heads: "{kind-plural} of {reference}" (§5.7). Reference is "types".
        SentenceRenderer.MemberSubject(Arch.Types.Members).ShouldBe("Members of types");
        SentenceRenderer.MemberSubject(Arch.Types.Methods).ShouldBe("Methods of types");
        SentenceRenderer.MemberSubject(Arch.Types.Properties).ShouldBe("Properties of types");
        SentenceRenderer.MemberSubject(Arch.Types.Fields).ShouldBe("Fields of types");
        SentenceRenderer.MemberSubject(Arch.Types.Events).ShouldBe("Events of types");
    }

    [Fact]
    public void MemberSubject_ReferenceIsUnderlyingTypeSelection()
    {
        // The {reference} is the source type selection in reference position (§6): a namespace locative.
        SentenceRenderer.MemberSubject(Arch.Namespace("MyApp.Web.*").Methods)
            .ShouldBe("Methods of types in `MyApp.Web.*`");
    }

    [Fact]
    public void Returning_SingleAnchor_RendersReturningClause()
    {
        SentenceRenderer.MemberSubject(Arch.Namespace("MyApp.Web.*").Methods.Returning(typeof(Task)))
            .ShouldBe("Methods of types in `MyApp.Web.*` returning `Task`");
    }

    [Fact]
    public void Returning_OpenGeneric_RendersDeclaredTypeParameterName()
    {
        // An open-generic anchor renders declared type-parameter names, like Implementing (§4.6, §5.2).
        SentenceRenderer.MemberSubject(Arch.Types.Methods.Returning(typeof(Task<>)))
            .ShouldBe("Methods of types returning `Task<TResult>`");
    }

    [Fact]
    public void Returning_MultipleAnchors_JoinWithOr()
    {
        SentenceRenderer.MemberSubject(Arch.Types.Methods.Returning(typeof(Task), typeof(Task<>)))
            .ShouldBe("Methods of types returning `Task` or `Task<TResult>`");
    }

    [Fact]
    public void MemberWhere_CanonicalizesToSentenceFinal_RegardlessOfChainPosition()
    {
        // The member Where renders sentence-final after the inline adjective, whatever the chain order.
        string whereFirst = SentenceRenderer.MemberSubject(
            Arch.Types.Methods.Where(m => m.IsAsync, "that are async").WithSuffix("Handler"));
        string whereLast = SentenceRenderer.MemberSubject(
            Arch.Types.Methods.WithSuffix("Handler").Where(m => m.IsAsync, "that are async"));

        whereFirst.ShouldBe("Methods of types named `*Handler` that are async");
        whereLast.ShouldBe(whereFirst);
    }

    [Fact]
    public void MemberAdjectives_RenderInAuthoringOrder()
    {
        // Two inline adjectives render in the order written — order is preserved, not canonicalized.
        SentenceRenderer.MemberSubject(Arch.Types.Methods.Returning(typeof(Task)).WithSuffix("Async"))
            .ShouldBe("Methods of types returning `Task` named `*Async`");
        SentenceRenderer.MemberSubject(Arch.Types.Methods.WithSuffix("Async").Returning(typeof(Task)))
            .ShouldBe("Methods of types named `*Async` returning `Task`");
    }

    [Fact]
    public void MemberWithNameMatching_RendersMatchesClauseAfterKindHead()
    {
        // The member .WithNameMatching adjective renders " whose name matches `glob`" inline after the
        // projection head (GRAMMAR §5.7): MemberWithNameMatchingAdjective.Fragment at an Inline placement,
        // cloned onto the selection through KindMemberSelection.Rebuild.
        SentenceRenderer.MemberSubject(Arch.Types.Members.WithNameMatching("*Handler*"))
            .ShouldBe("Members of types whose name matches `*Handler*`");
    }

    // ---- Registered noun + MustNotInject (GRAMMAR §4.7, §5.1, §5.3): head truth under adjectives ----

    [Fact]
    public void Registered_NoArg_RendersRegisteredTypesSubjectHead()
    {
        // The any-lifetime noun's subject head — the head IS the noun fragment (GRAMMAR §5.1), capitalized.
        SentenceRenderer.Subject(Arch.Registered()).ShouldBe("Registered types");
    }

    [Fact]
    public void Registered_WithLifetime_RendersLifetimePrefixedSubjectHead()
    {
        SentenceRenderer.Subject(Arch.Registered(Lifetime.Singleton)).ShouldBe("Singleton-registered types");
    }

    [Fact]
    public void MustNotInject_Flagship_RendersRegisteredSubjectAndTargets()
    {
        // The captive-dependency flagship (GRAMMAR §4.7): the Registered subject head survives, and the two
        // Registered operands render in reference position joined with "or".
        Constraint constraint = Arch.Registered(Lifetime.Singleton)
            .MustNotInject(Arch.Registered(Lifetime.Scoped), Arch.Registered(Lifetime.Transient));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Singleton-registered types must not inject scoped-registered types or transient-registered types.");
    }

    [Fact]
    public void MustNotInject_AdjectiveBearingRegisteredSubject_KeepsQualifierHead()
    {
        // Head truth under adjectives (GRAMMAR §5.1): an Except-bearing Registered subject keeps its qualifier
        // ("Singleton-registered types, except …") — never a false bare "Types, …". Except canonicalizes
        // sentence-final as usual.
        Selection exclusion = Arch.Type(typeof(SqlConnection));
        Constraint constraint = Arch.Registered(Lifetime.Singleton).Except(exclusion)
            .MustNotInject(Arch.Registered(Lifetime.Scoped));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Singleton-registered types, except `SqlConnection` must not inject scoped-registered types.");
    }
}
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
        SentenceRenderer.Subject(Arch.Layer("Domain", "MyApp.Domain.*"))
            .ShouldBe("The Domain layer");
    }

    [Fact]
    public void LayerSubjectWithAnyAdjective_SwitchesToTypesVoice()
    {
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Subject(web.WithSuffix("Controller"))
            .ShouldBe("Types in the Web layer named `*Controller`");
    }

    [Fact]
    public void BareTypesSubject_IsCapitalizedHead()
    {
        SentenceRenderer.Subject(Arch.Types)
            .ShouldBe("Types");
    }

    [Fact]
    public void NamespaceNounSubject_RendersLocative()
    {
        SentenceRenderer.Subject(Arch.Namespace("MyApp.*"))
            .ShouldBe("Types in `MyApp.*`");
    }

    [Fact]
    public void ProjectNounSubject_RendersProjectLocative()
    {
        SentenceRenderer.Subject(Arch.Project("MyApp.Web"))
            .ShouldBe("Types in project `MyApp.Web`");
    }

    [Theory]
    [InlineData(TypeKind.Class, "Classes")]
    [InlineData(TypeKind.Interface, "Interfaces")]
    [InlineData(TypeKind.Struct, "Structs")]
    [InlineData(TypeKind.Enum, "Enums")]
    [InlineData(TypeKind.Delegate, "Delegates")]
    public void OfKind_SubstitutesTheHeadPlural(TypeKind kind, string expectedHead)
    {
        SentenceRenderer.Subject(Arch.Types.OfKind(kind))
            .ShouldBe(expectedHead);
    }

    [Fact]
    public void WithPrefixSubjectHead_RendersNamedGlob()
    {
        SentenceRenderer.Subject(Arch.Types.WithPrefix("Legacy"))
            .ShouldBe("Types named `Legacy*`");
    }

    [Fact]
    public void WithNameMatchingSubjectHead_RendersMatchesClause()
    {
        SentenceRenderer.Subject(Arch.Types.WithNameMatching("*Repo*"))
            .ShouldBe("Types whose name matches `*Repo*`");
    }

    [Fact]
    public void DerivedFromSubjectHead_RendersDerivedClause()
    {
        SentenceRenderer.Subject(Arch.Types.DerivedFrom(typeof(ControllerBase)))
            .ShouldBe("Types derived from `ControllerBase`");
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
        string chainedBefore = SentenceRenderer.Subject(Arch.Types.Except(exclusion)
            .InNamespace("MyApp.*"));
        string chainedAfter = SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*")
            .Except(exclusion));

        chainedBefore.ShouldBe("Types in `MyApp.*`, except `SqlConnection`");
        chainedAfter.ShouldBe(chainedBefore);
    }

    // ---- The Except parenthetical and its closing comma (GRAMMAR §6). The fragment only OPENS the
    //      parenthetical; whatever junction follows the phrase is what closes it, and a sentence-final
    //      period or a bracketed tail closes it with no comma at all ----

    [Fact]
    public void Except_TheVerbClosesTheParenthetical()
    {
        // The junction: the verb straight after the subject.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(Arch.Type(typeof(SqlConnection)))
                .MustBeSealed())
            .ShouldBe("Types in `MyApp.*`, except `SqlConnection`, must be sealed.");
    }

    [Fact]
    public void Except_InTargetPosition_ThePeriodClosesIt()
    {
        // The junction: the end of the sentence, where the period closes the clause on its own.
        SentenceRenderer.Sentence(Arch.Types.MustNotReference(Arch.Types.InNamespace("MyApp.Legacy.*")
                .Except(Arch.Type(typeof(SqlConnection)))))
            .ShouldBe("Types must not reference types in `MyApp.Legacy.*`, except `SqlConnection`.");
    }

    [Fact]
    public void Except_OnThePenultimateTarget_TheListClosesItBeforeOr()
    {
        // The junction: the final " or " of a target list, the one list position that can follow an open
        // clause — every earlier item is already followed by a comma.
        SentenceRenderer.Sentence(Arch.Types.MustNotReference(
                Arch.Types.InNamespace("MyApp.Legacy.*").Except(Arch.Type(typeof(SqlConnection))),
                Arch.Type(typeof(SqlCommand))))
            .ShouldBe("Types must not reference types in `MyApp.Legacy.*`, except `SqlConnection`, or `SqlCommand`.");
    }

    [Fact]
    public void Except_OnAMemberSubjectsSource_ClosesBeforeTheMemberClauses()
    {
        // The junction: the member subject's own clauses, which render after the source reference.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(Arch.Type(typeof(SqlConnection)))
                .Methods.Returning(typeof(Task))
                .MustHaveSuffix("Async"))
            .ShouldBe("Methods of types in `MyApp.*`, except `SqlConnection`, returning `Task` must be named `*Async`.");
    }

    [Fact]
    public void Except_OnAMemberSubjectsSourceWithNoMemberClauses_TheVerbClosesIt()
    {
        // The junction: the verb again — with no member clause between them, the reference ends the subject.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(Arch.Type(typeof(SqlConnection)))
                .Methods.MustBePublic())
            .ShouldBe("Methods of types in `MyApp.*`, except `SqlConnection`, must be public.");
    }

    [Fact]
    public void Except_OnTheLastOperandOfAnOrJoinedUnion_TheVerbClosesIt()
    {
        // The junction: the verb, reached through a union that does not collapse — the phrase ends with its
        // last operand, so the operand's open clause is the union's own.
        SentenceRenderer.Sentence(Arch.AnyOf(
                    Arch.Project("A"),
                    Arch.Namespace("B.*").Except(Arch.Type(typeof(SqlConnection))))
                .MustBeSealed())
            .ShouldBe("Types in project `A` or types in `B.*`, except `SqlConnection`, must be sealed.");
    }

    [Fact]
    public void Except_OnThePenultimateOperandOfAnOrJoinedUnion_ClosesBeforeOr()
    {
        // The junction: the union's own final " or ", the operand-list twin of the target list above.
        SentenceRenderer.Sentence(Arch.AnyOf(
                    Arch.Namespace("B.*").Except(Arch.Type(typeof(SqlConnection))),
                    Arch.Project("A"))
                .MustBeSealed())
            .ShouldBe("Types in `B.*`, except `SqlConnection`, or types in project `A` must be sealed.");
    }

    [Fact]
    public void Except_BeforeAVerbPhraseTail_ClosesBeforeTheTail()
    {
        // The junction: a verb-phrase tail after the target list.
        SentenceRenderer.Sentence(Arch.Types.MustNotCatchUnfiltered(Arch.Types.InNamespace("MyApp.Errors.*")
                .Except(Arch.Type(typeof(TimeoutException)))))
            .ShouldBe(
                "Types must not catch types in `MyApp.Errors.*`, except `TimeoutException`, "
                + "without a `when` filter.");
    }

    [Fact]
    public void Except_BeforeABracketedTail_NeedsNoComma()
    {
        // The junction that needs nothing: a bracketed parenthetical closes the clause on its own.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .MustOnlyReference(Arch.Types.InNamespace("MyApp.Domain.*")
                    .Except(Arch.Type(typeof(SqlConnection)))))
            .ShouldBe(
                "Types in `MyApp.*` must reference only types in `MyApp.Domain.*`, except `SqlConnection` "
                + "(external packages are not constrained by this rule).");
    }

    // ---- Except over several operands (GRAMMAR §5.1, §5.2): the union arch.AnyOf would mint ----

    [Fact]
    public void Except_SeveralSelections_ExcludesTheirUnion()
    {
        // The payload is the union, so it renders through the namespace-noun collapse.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(Arch.Namespace("MyApp.Legacy.*"), Arch.Namespace("MyApp.Generated.*"))
                .MustBeSealed())
            .ShouldBe(
                "Types in `MyApp.*`, except types in `MyApp.Legacy.*` or `MyApp.Generated.*`, must be sealed.");
    }

    [Fact]
    public void Except_SeveralTypes_ExcludesTheirUnion()
    {
        // The typeof sugar wraps each type as a bare type noun, which collapses to the backticked or-list.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(typeof(SqlConnection), typeof(SqlCommand))
                .MustBeSealed())
            .ShouldBe("Types in `MyApp.*`, except `SqlConnection` or `SqlCommand`, must be sealed.");
    }

    // ---- The Named adjective (GRAMMAR §5.2): the exact-name form beside WithNameMatching ----

    [Fact]
    public void Named_OneName_RendersAnInlineNamedClause()
    {
        SentenceRenderer.Subject(Arch.Types.Named("Program"))
            .ShouldBe("Types named `Program`");
    }

    [Fact]
    public void Named_SeveralNames_OrJoinTheNames()
    {
        SentenceRenderer.Subject(Arch.Types.Named("A", "B", "C"))
            .ShouldBe("Types named `A`, `B` or `C`");
    }

    [Fact]
    public void Named_InExceptPosition_ReadsAsTheSetItExcludes()
    {
        // Why the clause is inline rather than a bare backticked name: in reference position it must still
        // say "types named `X`", the set, not the one type an arch.Type noun would name.
        SentenceRenderer.Sentence(Arch.Types.InNamespace("MyApp.*")
                .Except(Arch.Types.Named("SystemClock"))
                .MustBeSealed())
            .ShouldBe("Types in `MyApp.*`, except types named `SystemClock`, must be sealed.");
    }

    [Fact]
    public void Named_InTargetPosition_ReadsAsAListItem()
    {
        SentenceRenderer.Sentence(Arch.Types.MustNotConstruct(
                Arch.Type(typeof(SqlConnection)),
                Arch.Types.Named("SessionStore", "ModelCache")))
            .ShouldBe("Types must not construct `SqlConnection` or types named `SessionStore` or `ModelCache`.");
    }

    [Fact]
    public void Authored_RendersTheHeadPremodifier()
    {
        // Arrange
        Selection namespaced = Arch.Types.InNamespace("MyApp.*")
            .Authored();
        Selection layer = Arch.Layer("Domain", "MyApp.Domain.*")
            .Authored();

        // Act
        string namespacedSubject = SentenceRenderer.Subject(namespaced);
        string layerSubject = SentenceRenderer.Subject(layer);

        // Assert — the prefix rides in front of the head, and (like any adjective) it switches a bare
        // layer out of collective voice.
        namespacedSubject.ShouldBe("Authored types in `MyApp.*`");
        layerSubject.ShouldBe("Authored types in the Domain layer");
    }

    [Fact]
    public void Authored_ComposesWithOfKind_RegardlessOfChainPosition()
    {
        // Arrange — a premodifier composes with the head OfKind substitutes rather than clobbering it.
        Selection kindFirst = Arch.Types.InNamespace("MyApp.*")
            .OfKind(TypeKind.Interface)
            .Authored();
        Selection authoredFirst = Arch.Types.InNamespace("MyApp.*")
            .Authored()
            .OfKind(TypeKind.Interface);

        // Act
        string kindFirstSubject = SentenceRenderer.Subject(kindFirst);
        string authoredFirstSubject = SentenceRenderer.Subject(authoredFirst);

        // Assert
        kindFirstSubject.ShouldBe("Authored interfaces in `MyApp.*`");
        authoredFirstSubject.ShouldBe(kindFirstSubject);
    }

    [Fact]
    public void Where_RendersDescriptionAsSentenceFinalRelativeClause()
    {
        Selection selection = Arch.Types.InNamespace("MyApp.*")
            .Where(t => t.Name.Any(char.IsDigit), "whose name contains a digit");
        SentenceRenderer.Subject(selection)
            .ShouldBe("Types in `MyApp.*` whose name contains a digit");
    }

    [Fact]
    public void CollidingSimpleNames_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotReference(typeof(Order), typeof(Stubs.Sales.Order));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not reference `Billing.Order` or `Sales.Order`.");
    }

    [Fact]
    public void TwoTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotReference(typeof(SqlConnection), typeof(ControllerBase));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not reference `SqlConnection` or `ControllerBase`.");
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
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not use `DateTime.Now` or `DateTime.UtcNow`.");
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
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not use `Task.Wait()`.");
    }

    [Fact]
    public void MustNotUse_GenericAnchorProperty_RendersDeclaredTypeParameterName()
    {
        Constraint constraint = Arch.Types.MustNotUse(Arch.Member(typeof(Task<>), "Result"));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not use `Task<TResult>.Result`.");
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
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not construct types in `MyApp.Services.*`.");
    }

    [Fact]
    public void MustNotConstruct_TypeSugar_BackticksSimpleName()
    {
        // The Type sugar overload wraps the bare type as a single-type selection (arch.Type written for you).
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(SqlConnection));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not construct `SqlConnection`.");
    }

    [Fact]
    public void MustNotConstruct_MultipleTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(SqlConnection), typeof(ControllerBase));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not construct `SqlConnection` or `ControllerBase`.");
    }

    [Fact]
    public void MustNotConstruct_CollidingSimpleNames_QualifyWithMinimalTrailingSegments()
    {
        // Shares TargetList with the reference verbs, so colliding simple names widen identically.
        Constraint constraint = Arch.Types.MustNotConstruct(typeof(Order), typeof(Stubs.Sales.Order));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not construct `Billing.Order` or `Sales.Order`.");
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
        SentenceRenderer.Sentence(web.WithSuffix("Controller")
                .MustNotCatch(typeof(Exception)))
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
    public void MustNotCatchUnfiltered_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6) survives the trailing filter qualifier — the verb phrase closes the sentence.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustNotCatchUnfiltered(typeof(Exception)))
            .ShouldBe("The Web layer must not catch `Exception` without a `when` filter.");
    }

    [Fact]
    public void MustNotCatchUnfiltered_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        // Head truth under adjectives (§6): a WithSuffix-bearing layer subject switches to types voice.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.WithSuffix("Controller")
                .MustNotCatchUnfiltered(typeof(Exception)))
            .ShouldBe("Types in the Web layer named `*Controller` must not catch `Exception` without a `when` filter.");
    }

    [Fact]
    public void MustNotCatchUnfiltered_MultipleTargets_JoinWithOr()
    {
        // The target list joins before the filter qualifier — never one qualifier per target.
        Constraint constraint = Arch.Types.MustNotCatchUnfiltered(typeof(InvalidOperationException), typeof(TimeoutException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not catch `InvalidOperationException` or `TimeoutException` without a `when` filter.");
    }

    [Fact]
    public void MustNotCatchUnfiltered_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        // Shares TargetList with the other catch verb, so colliding exception names widen the same way.
        Constraint constraint = Arch.Types.MustNotCatchUnfiltered(typeof(DataException), typeof(Stubs.Sales.DataException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not catch `Billing.DataException` or `Sales.DataException` without a `when` filter.");
    }

    [Fact]
    public void MustNotSwallow_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6): the third catch verb closes on its target list, with no trailing qualifier at all.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustNotSwallow(typeof(Exception)))
            .ShouldBe("The Web layer must not swallow `Exception`.");
    }

    [Fact]
    public void MustNotSwallow_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        // Head truth under adjectives (§6): a WithSuffix-bearing layer subject switches to types voice.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.WithSuffix("Controller")
                .MustNotSwallow(typeof(Exception)))
            .ShouldBe("Types in the Web layer named `*Controller` must not swallow `Exception`.");
    }

    [Fact]
    public void MustNotSwallow_MultipleTargets_JoinWithOr()
    {
        Constraint constraint = Arch.Types.MustNotSwallow(typeof(InvalidOperationException), typeof(TimeoutException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not swallow `InvalidOperationException` or `TimeoutException`.");
    }

    [Fact]
    public void MustNotSwallow_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        // Shares TargetList with the other catch verbs, so colliding exception names widen the same way.
        Constraint constraint = Arch.Types.MustNotSwallow(typeof(DataException), typeof(Stubs.Sales.DataException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not swallow `Billing.DataException` or `Sales.DataException`.");
    }

    [Fact]
    public void MustNotThrow_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6): a bare layer subject speaks collectively — "The Domain layer must not throw …".
        Layer domain = Arch.Layer("Domain", "MyApp.Domain.*");
        SentenceRenderer.Sentence(domain.MustNotThrow(typeof(Exception)))
            .ShouldBe("The Domain layer must not throw `Exception`.");
    }

    [Fact]
    public void MustNotThrow_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        // Head truth under adjectives (§6): the layer subject switches to types voice under WithSuffix.
        Layer domain = Arch.Layer("Domain", "MyApp.Domain.*");
        SentenceRenderer.Sentence(domain.WithSuffix("Service")
                .MustNotThrow(typeof(Exception)))
            .ShouldBe("Types in the Domain layer named `*Service` must not throw `Exception`.");
    }

    [Fact]
    public void MustNotThrow_ThreeTargets_JoinWithCommasAndOr_NoOxfordComma()
    {
        // Shares TargetList with the reference verbs: three targets join "`A`, `B` or `C`" with no Oxford comma.
        Constraint constraint = Arch.Types.MustNotThrow(
            typeof(Exception), typeof(SystemException), typeof(ApplicationException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not throw `Exception`, `SystemException` or `ApplicationException`.");
    }

    [Fact]
    public void MustNotThrow_CollidingTargets_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotThrow(typeof(DataException), typeof(Stubs.Sales.DataException));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not throw `Billing.DataException` or `Sales.DataException`.");
    }

    [Fact]
    public void MustOnlyThrow_NamespaceSubject_RendersLocativeAndStrictAllowlist()
    {
        // The namespace-locative subject + the strict throw allowlist: exact equality proves no external-
        // packages caveat rides along (unlike MustOnlyReference), which is the strictness rendering (§5.3).
        Constraint constraint = Arch.Namespace("MyApp.Domain.*")
            .MustOnlyThrow(typeof(InvalidOperationException));
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
        SentenceRenderer.MemberSubject(Arch.Types.Members)
            .ShouldBe("Members of types");
        SentenceRenderer.MemberSubject(Arch.Types.Methods)
            .ShouldBe("Methods of types");
        SentenceRenderer.MemberSubject(Arch.Types.Properties)
            .ShouldBe("Properties of types");
        SentenceRenderer.MemberSubject(Arch.Types.Fields)
            .ShouldBe("Fields of types");
        SentenceRenderer.MemberSubject(Arch.Types.Events)
            .ShouldBe("Events of types");
    }

    [Fact]
    public void MemberSubject_ReferenceIsUnderlyingTypeSelection()
    {
        // The {reference} is the source type selection in reference position (§6): a namespace locative.
        SentenceRenderer.MemberSubject(Arch.Namespace("MyApp.Web.*")
                .Methods)
            .ShouldBe("Methods of types in `MyApp.Web.*`");
    }

    [Fact]
    public void Returning_SingleAnchor_RendersReturningClause()
    {
        SentenceRenderer.MemberSubject(Arch.Namespace("MyApp.Web.*")
                .Methods.Returning(typeof(Task)))
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
            Arch.Types.Methods.Where(m => m.IsAsync, "that are async")
                .WithSuffix("Handler"));
        string whereLast = SentenceRenderer.MemberSubject(
            Arch.Types.Methods.WithSuffix("Handler")
                .Where(m => m.IsAsync, "that are async"));

        whereFirst.ShouldBe("Methods of types named `*Handler` that are async");
        whereLast.ShouldBe(whereFirst);
    }

    [Fact]
    public void MemberAdjectives_RenderInAuthoringOrder()
    {
        // Two inline adjectives render in the order written — order is preserved, not canonicalized.
        SentenceRenderer.MemberSubject(Arch.Types.Methods.Returning(typeof(Task))
                .WithSuffix("Async"))
            .ShouldBe("Methods of types returning `Task` named `*Async`");
        SentenceRenderer.MemberSubject(Arch.Types.Methods.WithSuffix("Async")
                .Returning(typeof(Task)))
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
        SentenceRenderer.Subject(Arch.Registered())
            .ShouldBe("Registered types");
    }

    [Fact]
    public void Registered_WithLifetime_RendersLifetimePrefixedSubjectHead()
    {
        SentenceRenderer.Subject(Arch.Registered(Lifetime.Singleton))
            .ShouldBe("Singleton-registered types");
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
        Constraint constraint = Arch.Registered(Lifetime.Singleton)
            .Except(exclusion)
            .MustNotInject(Arch.Registered(Lifetime.Scoped));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Singleton-registered types, except `SqlConnection`, must not inject scoped-registered types.");
    }

    // ---- Surface union: arch.AnyOf (GRAMMAR §5.1, §6). A homogeneous union hoists one head and locative
    //      and or-joins the operand names; anything else or-joins the operands' own phrases ----

    [Fact]
    public void UnionOfProjects_CollapsesToOneHeadAndLocative()
    {
        // The flagship shape: four project operands read as one locative, not four repeated phrases.
        Selection union = Arch.AnyOf(
            Arch.Project("A"), Arch.Project("B"), Arch.Project("C"), Arch.Project("D"));
        SentenceRenderer.Subject(union)
            .ShouldBe("Types in projects `A`, `B`, `C` or `D`");
    }

    [Fact]
    public void UnionOfNamespaces_CollapsesToTheSharedLocative()
    {
        SentenceRenderer.Subject(Arch.AnyOf(Arch.Namespace("A.*"), Arch.Namespace("B.*")))
            .ShouldBe("Types in `A.*` or `B.*`");
    }

    [Fact]
    public void UnionOfTypes_InReferencePosition_IsTheBareBacktickedList()
    {
        Selection union = Arch.AnyOf(Arch.Type(typeof(SqlConnection)), Arch.Type(typeof(ControllerBase)));
        SentenceRenderer.Reference(union)
            .ShouldBe("`SqlConnection` or `ControllerBase`");
    }

    [Fact]
    public void UnionOfTypes_CollidingSimpleNames_QualifyWithMinimalTrailingSegments()
    {
        // The collapsed type list reuses ProseFormat.TypeList, so a union disambiguates by exactly the rule
        // every other multi-operand list uses (§6).
        SentenceRenderer.Reference(Arch.AnyOf(typeof(Order), typeof(Stubs.Sales.Order)))
            .ShouldBe("`Billing.Order` or `Sales.Order`");
    }

    [Fact]
    public void UnionOfBareLayers_SpeaksCollectivelyInThePlural()
    {
        Selection union = Arch.AnyOf(Arch.Layer("UnionDomain", "MyApp.Domain.*"), Arch.Layer("UnionWeb", "MyApp.Web.*"));
        SentenceRenderer.Subject(union)
            .ShouldBe("The UnionDomain or UnionWeb layers");
    }

    [Fact]
    public void UnionMemberSubject_TakesTheCollapsedReference()
    {
        // The path the dogfood rule actually takes: a member subject renders "methods of {reference}".
        SentenceRenderer.MemberSubject(Arch.AnyOf(Arch.Project("A"), Arch.Project("B"))
                .Methods)
            .ShouldBe("Methods of types in projects `A` or `B`");
    }

    [Fact]
    public void UnionExcept_CanonicalizesSentenceFinalAfterTheCollapsedLocative()
    {
        // Adjectives attach to the union, not through it: (a ∪ b) − c, rendered against the hoisted head.
        Selection union = Arch.AnyOf(Arch.Project("A"), Arch.Project("B"))
            .Except(Arch.Type(typeof(SqlConnection)));
        SentenceRenderer.Subject(union)
            .ShouldBe("Types in projects `A` or `B`, except `SqlConnection`");
    }

    [Fact]
    public void UnionOfKind_SubstitutesTheHoistedHead()
    {
        Selection union = Arch.AnyOf(Arch.Project("A"), Arch.Project("B"))
            .OfKind(TypeKind.Interface);
        SentenceRenderer.Subject(union)
            .ShouldBe("Interfaces in projects `A` or `B`");
    }

    [Fact]
    public void UnionAuthored_PrefixesTheCollapsedHead()
    {
        // The premodifier assembles against the hoisted head exactly as a substitution does — and in
        // reference position too, which is the shape a member subject renders its type selection in.
        Selection union = Arch.AnyOf(Arch.Project("A"), Arch.Project("B"))
            .Authored();
        SentenceRenderer.Subject(union)
            .ShouldBe("Authored types in projects `A` or `B`");
        SentenceRenderer.Reference(union)
            .ShouldBe("authored types in projects `A` or `B`");
    }

    [Fact]
    public void UnionAuthored_OverAFallbackUnion_DistributesThePrefixAcrossOperands()
    {
        // Same contract as the head adjective: a union that does not collapse distributes the prefix into
        // its operands, so the filter the checker applies always reaches the sentence.
        Selection union = Arch.AnyOf(Arch.Project("A"), Arch.Namespace("B.*"))
            .Authored();
        SentenceRenderer.Subject(union)
            .ShouldBe("Authored types in project `A` or authored types in `B.*`");
    }

    [Fact]
    public void UnionOfMixedNounKinds_FallsBackToOrJoinedPhrases()
    {
        // The fallback is a decision, not an accident: mixed noun kinds have no shared locative to hoist.
        SentenceRenderer.Subject(Arch.AnyOf(Arch.Project("A"), Arch.Namespace("B.*")))
            .ShouldBe("Types in project `A` or types in `B.*`");
    }

    [Fact]
    public void UnionWithAnAdjectiveBearingOperand_FallsBackToOrJoinedPhrases()
    {
        // An operand carrying its own adjective cannot fold into a shared locative, so the whole union falls back.
        Selection union = Arch.AnyOf(Arch.Project("A")
            .WithSuffix("Controller"), Arch.Project("B"));
        SentenceRenderer.Subject(union)
            .ShouldBe("Types in project `A` named `*Controller` or types in project `B`");
    }

    [Fact]
    public void UnionOfKind_OverAFallbackUnion_DistributesTheHeadAcrossOperands()
    {
        // A head adjective on a union that does not collapse still reaches the prose — the checker applies
        // the kind filter, so the sentence must say so rather than silently reading "types".
        Selection union = Arch.AnyOf(Arch.Project("A"), Arch.Namespace("B.*"))
            .OfKind(TypeKind.Interface);
        SentenceRenderer.Subject(union)
            .ShouldBe("Interfaces in project `A` or interfaces in `B.*`");
    }

    [Fact]
    public void SingleOperandUnion_RendersExactlyAsTheBareOperand()
    {
        // Selections are loop-buildable (§2 principle 5), so a loop yielding one operand is legal — and an
        // identity in both positions, not a degenerate "or" list.
        SentenceRenderer.Subject(Arch.AnyOf(Arch.Project("A")))
            .ShouldBe("Types in project `A`");
        SentenceRenderer.Reference(Arch.AnyOf(Arch.Project("A")))
            .ShouldBe("types in project `A`");
    }

    [Fact]
    public void UnionSubject_RendersThroughTheFullSentence()
    {
        // End to end: the union reaches the law sentence like any other subject.
        Constraint constraint = Arch.AnyOf(Arch.Project("A"), Arch.Project("B"))
            .MustBeSealed();
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types in projects `A` or `B` must be sealed.");
    }

    // ---- String attribute anchors (GRAMMAR §5.2–§5.3, §6): a name-anchored attribute assembles exactly as
    //      its typeof twin does — same head, same bracket, same collision widening ----

    [Fact]
    public void AttributedWithStringSubjectHead_StripsAttributeAndBrackets()
    {
        // The subject-head twin of AttributedWithSubjectHead_StripsAttributeAndBrackets, reached by string.
        SentenceRenderer.Subject(Arch.Types.AttributedWith("Zphil.LoadBearing.Tests.Stubs.ApiControllerAttribute"))
            .ShouldBe("Types attributed with `[ApiController]`");
    }

    [Fact]
    public void MustNotBeAttributedWith_CollidingStringAnchors_WidenInsideTheBrackets()
    {
        // Widening runs off the anchor's dot path, so string anchors collide and widen exactly as the typeof
        // pair does — `[Billing.Audit]` / `[Sales.Audit]`, not a bare `[Audit]`.
        Constraint constraint = Arch.Types.MustNotBeAttributedWith(
            "Zphil.LoadBearing.Tests.Stubs.Billing.AuditAttribute", "Zphil.LoadBearing.Tests.Stubs.Sales.AuditAttribute");
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not be attributed with `[Billing.Audit]` or `[Sales.Audit]`.");
    }

    [Fact]
    public void MustBeAttributedWith_GenericDefinitionString_KeepsTheSuffixInsideTheBrackets()
    {
        // A generic definition's last segment is `MarkAttribute<T>`, which does not END with "Attribute", so
        // nothing is stripped — the suffix rule reads the rendered segment, never the name before the angle
        // brackets.
        SentenceRenderer.Sentence(Arch.Types.MustBeAttributedWith("N.MarkAttribute<T>"))
            .ShouldBe("Types must be attributed with `[MarkAttribute<T>]`.");
    }

    [Fact]
    public void MustBeAttributedWith_ClosedGenericString_SplitsOnDotsOutsideTheBrackets()
    {
        // The dots inside `<...>` belong to the argument's own path: a naive last-dot split would render
        // `[Int32>]`. (The spelling names a construction, so it matches nothing — it still has to render.)
        SentenceRenderer.Sentence(Arch.Types.MustBeAttributedWith("N.MarkAttribute<System.Int32>"))
            .ShouldBe("Types must be attributed with `[MarkAttribute<System.Int32>]`.");
    }

    // ---- String hierarchy anchors (GRAMMAR §5.2–§5.3, §6): the same equivalence in interface and base-type
    //      position — same fragment, same collision widening, no brackets ----

    [Fact]
    public void ImplementingStringSubjectHead_RendersTheTypeofHead()
    {
        // The subject-head twin, reached by string. An open generic renders declared type-parameter names on
        // both arms, because the string IS the name a report prints.
        SentenceRenderer.Subject(Arch.Types.Implementing("Zphil.LoadBearing.Tests.Stubs.IHandler<T>"))
            .ShouldBe(SentenceRenderer.Subject(Arch.Types.Implementing(typeof(IHandler<>))));
    }

    [Fact]
    public void DerivedFromStringSubjectHead_RendersTheTypeofHead()
    {
        SentenceRenderer.Subject(Arch.Types.DerivedFrom("Zphil.LoadBearing.Tests.Stubs.ControllerBase"))
            .ShouldBe(SentenceRenderer.Subject(Arch.Types.DerivedFrom(typeof(ControllerBase))));
    }

    [Fact]
    public void MustNotImplement_CollidingStringAnchors_QualifyWithMinimalTrailingSegments()
    {
        // Widening runs off the anchor's dot path, so string anchors collide and widen exactly as the typeof
        // pair does in MustNotImplement_CollidingAnchors_QualifyWithMinimalTrailingSegments.
        Constraint constraint = Arch.Types.MustNotImplement(
            "Zphil.LoadBearing.Tests.Stubs.Billing.IReceipt", "Zphil.LoadBearing.Tests.Stubs.Sales.IReceipt");
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not implement `Billing.IReceipt` or `Sales.IReceipt`.");
    }

    [Fact]
    public void MustNotDeriveFrom_CollidingStringAnchors_QualifyWithMinimalTrailingSegments()
    {
        Constraint constraint = Arch.Types.MustNotDeriveFrom(
            "Zphil.LoadBearing.Tests.Stubs.Billing.LedgerBase", "Zphil.LoadBearing.Tests.Stubs.Sales.LedgerBase");
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must not derive from `Billing.LedgerBase` or `Sales.LedgerBase`.");
    }

    [Fact]
    public void MustImplement_ClosedGenericString_SplitsOnDotsOutsideTheAngleBrackets()
    {
        // The dots inside `<...>` belong to the argument's own path: a naive last-dot split would render
        // `Int32>`. (The spelling names a construction, so it matches nothing — it still has to render.)
        SentenceRenderer.Sentence(Arch.Types.MustImplement("N.IHandler<System.Int32>"))
            .ShouldBe("Types must implement `IHandler<System.Int32>`.");
    }

    // ---- The member attribute adjective (GRAMMAR §5.7, §6): a HEAD PREMODIFIER, so a member-attributed
    //      subject and a type-attributed one never render the same sentence ----

    [Fact]
    public void MemberAttributedWith_RendersTheDogfoodSubject()
    {
        // The rule this axis exists for, in this repository's own spec: the MCP tool methods, not the types
        // that happen to declare them. Reached by string, because a spec need not reference the attribute's
        // package to name it.
        SentenceRenderer.MemberSubject(Arch.Namespace("Zphil.LoadBearing.*")
                .Methods
                .AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute"))
            .ShouldBe("`[McpServerTool]`-attributed methods of types in `Zphil.LoadBearing.*`");
    }

    [Fact]
    public void MemberAttributedWith_ComposesWithInlineAndSentenceFinalAdjectives()
    {
        // The prefix leads, the inline adjective follows the type reference, the Where canonicalizes
        // sentence-final — three placements in one subject. (That `.Returning` is still reachable after the
        // adjective is the TSelf-generic shape holding: this line would not compile otherwise.)
        SentenceRenderer.MemberSubject(Arch.Types.Methods
                .AttributedWith(typeof(ApiControllerAttribute))
                .Returning(typeof(Task))
                .Where(m => m.IsAsync, "that are async"))
            .ShouldBe("`[ApiController]`-attributed methods of types returning `Task` that are async");
    }

    [Fact]
    public void MemberAttributedWith_AndTypeAttributedThenProjected_RenderDifferentSentences()
    {
        // THE disambiguation pin — the whole reason the member adjective premodifies. Two different subjects
        // (every method of an attributed type, versus the attributed methods of any type); an inline member
        // fragment would give them one byte-identical sentence.
        string typeAttributed = SentenceRenderer.MemberSubject(Arch.Types.AttributedWith(typeof(ApiControllerAttribute))
            .Methods);
        string memberAttributed = SentenceRenderer.MemberSubject(Arch.Types.Methods.AttributedWith(typeof(ApiControllerAttribute)));

        typeAttributed.ShouldBe("Methods of types attributed with `[ApiController]`");
        memberAttributed.ShouldBe("`[ApiController]`-attributed methods of types");
        memberAttributed.ShouldNotBe(typeAttributed);
    }

    [Fact]
    public void MemberAttributedWith_StackedPrefixes_BothSurfaceInAuthoringOrder()
    {
        // Stacked prefixes are an INTERSECTION: a member narrowed by two attribute adjectives carries both,
        // so both must reach the sentence. Dropping either would describe a wider subject than the checker
        // uses. (The type side substitutes its single head prefix; the member side accumulates.)
        SentenceRenderer.MemberSubject(Arch.Types.Methods
                .AttributedWith(typeof(ApiControllerAttribute))
                .AttributedWith(typeof(AuditAttribute)))
            .ShouldBe("`[ApiController]`-attributed `[Audit]`-attributed methods of types");
    }

    // ---- The member mutability verbs and the static adjective (GRAMMAR §5.7, §6). A member subject never
    //      speaks in collective layer voice: the layer sits in REFERENCE position, so the type-subject
    //      bare-layer/adjective-bearing voice switch has nothing to switch here ----

    [Fact]
    public void MustBeGetOnly_MemberSubjectOverALayer_RendersInMemberVoice()
    {
        Layer domain = Arch.Layer("Domain", "MyApp.Domain.*");
        SentenceRenderer.Sentence(domain.Properties.MustBeGetOnly())
            .ShouldBe("Properties of the Domain layer must be get-only.");
    }

    [Fact]
    public void ThatAreStatic_PremodifiesTheKindPlural()
    {
        // The head prefix leads the kind plural, and the layer stays in reference position behind it.
        Layer core = Arch.Layer("Core", "MyApp.Core.*");
        SentenceRenderer.Sentence(core.Fields.ThatAreStatic()
                .MustBeReadonly())
            .ShouldBe("Static fields of the Core layer must be readonly.");
    }

    [Fact]
    public void ThatAreStaticAndAttributedWith_HeadPrefixesConcatenateInAuthoringOrder()
    {
        // Two head prefixes from DIFFERENT families now meet for the first time, and they concatenate rather
        // than one winning: stacked prefixes are an INTERSECTION, so both must reach the sentence or it would
        // describe a wider subject than the checker uses. Authoring order decides which leads — pinned in
        // both orders, because the order is the observable fact and neither reading is the renderer's to
        // canonicalize.
        SentenceRenderer.MemberSubject(Arch.Types.Fields.AttributedWith(typeof(AuditAttribute))
                .ThatAreStatic())
            .ShouldBe("`[Audit]`-attributed static fields of types");

        SentenceRenderer.MemberSubject(Arch.Types.Fields.ThatAreStatic()
                .AttributedWith(typeof(AuditAttribute)))
            .ShouldBe("Static `[Audit]`-attributed fields of types");
    }

    [Fact]
    public void ThatAreStatic_WithMemberWhere_KeepsTheWhereSentenceFinal()
    {
        // Three placements in one member subject: the prefix leads, the type reference follows the kind
        // plural, and the Where canonicalizes sentence-final whatever the chain order.
        SentenceRenderer.MemberSubject(Arch.Types.Fields
                .Where(m => !m.IsConst, "that are not constants")
                .ThatAreStatic())
            .ShouldBe("Static fields of types that are not constants");
    }

    // ---- The coverage verb (GRAMMAR §5.3, §6, §10): its memberships render in reference position through
    //      the SHARED target list, so layers read as layers and colliding types widen like any other list ----

    [Fact]
    public void MustBelongTo_BareLayerSubject_SpeaksCollectively()
    {
        // Layer voice (§6): a bare layer subject speaks collectively on the coverage verb too, and a single
        // membership joins to itself — no degenerate "or" list.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustBelongTo(Arch.Layer("Application", "MyApp.*")))
            .ShouldBe("The Web layer must belong to the Application layer.");
    }

    [Fact]
    public void MustBelongTo_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        // Head truth under adjectives (§6): a WithSuffix-bearing layer subject switches to types voice, while
        // the membership keeps its own collective reference fragment on the far side of the verb.
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.WithSuffix("Controller")
                .MustBelongTo(Arch.Layer("Application", "MyApp.*")))
            .ShouldBe("Types in the Web layer named `*Controller` must belong to the Application layer.");
    }

    [Fact]
    public void MustBelongTo_ThreeMemberships_JoinWithCommasAndOr_NoOxfordComma()
    {
        // Shares TargetList with the dependency verbs, so three memberships join "`A`, `B` or `C`" with no
        // Oxford comma — and the or-join is what says the rule is satisfied by ANY one of them.
        Constraint constraint = Arch.Types.MustBelongTo(
            Arch.Layer("Core", "MyApp.Core.*"),
            Arch.Layer("Host", "MyApp.Host.*"),
            Arch.Layer("Adapter", "MyApp.Adapter.*"));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must belong to the Core layer, the Host layer or the Adapter layer.");
    }

    [Fact]
    public void MustBelongTo_CollidingTypeMemberships_QualifyWithMinimalTrailingSegments()
    {
        // The list is genuinely the shared one: a type-selection membership widens by the same minimal-
        // trailing-segments rule the dependency target lists use.
        Constraint constraint = Arch.Types.MustBelongTo(
            Arch.Type(typeof(Order)), Arch.Type(typeof(Stubs.Sales.Order)));
        SentenceRenderer.Sentence(constraint)
            .ShouldBe("Types must belong to `Billing.Order` or `Sales.Order`.");
    }

    // ---- Project residence (GRAMMAR §5.3, §6): the verb names the project axis in its own phrase, so a
    //      project-noun subject and a project-named verb can meet in one sentence ----

    [Fact]
    public void MustResideInProject_BareLayerSubject_SpeaksCollectively()
    {
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.MustResideInProject("MyApp.Web"))
            .ShouldBe("The Web layer must reside in project `MyApp.Web`.");
    }

    [Fact]
    public void MustResideInProject_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        Layer web = Arch.Layer("Web", "MyApp.Web.*");
        SentenceRenderer.Sentence(web.WithSuffix("Controller")
                .MustResideInProject("MyApp.Web"))
            .ShouldBe("Types in the Web layer named `*Controller` must reside in project `MyApp.Web`.");
    }

    [Fact]
    public void MustResideInProject_ProjectSubject_RendersBothProjectNames()
    {
        // The one sentence where the project noun's locative and the verb's own project phrase meet: the
        // subject names where the types are, the verb names where they must be, and both survive.
        SentenceRenderer.Sentence(Arch.Project("A")
                .MustResideInProject("B"))
            .ShouldBe("Types in project `A` must reside in project `B`.");
    }

    // ---- Registration completeness (GRAMMAR §5.3, §4.7, §6): a nullary verb, so the whole sentence past
    //      "must" is fixed and every reading difference lives in the subject ----

    [Fact]
    public void MustBeRegistered_BareLayerSubject_SpeaksCollectively()
    {
        Layer services = Arch.Layer("Services", "MyApp.Services.*");
        SentenceRenderer.Sentence(services.MustBeRegistered())
            .ShouldBe("The Services layer must be registered.");
    }

    [Fact]
    public void MustBeRegistered_AdjectiveBearingLayerSubject_SwitchesToTypesVoice()
    {
        Layer services = Arch.Layer("Services", "MyApp.Services.*");
        SentenceRenderer.Sentence(services.WithSuffix("Service")
                .MustBeRegistered())
            .ShouldBe("Types in the Services layer named `*Service` must be registered.");
    }

    [Fact]
    public void MustBeRegistered_RegisteredSubject_KeepsTheLifetimeQualifiedHead()
    {
        // Degenerate but legal, and the reason it is pinned: the Registered noun's head IS its fragment
        // (§5.1), so a subject that already names the registration fact keeps that head in front of the
        // verb that tests it — never a false bare "Types must be registered."
        SentenceRenderer.Sentence(Arch.Registered(Lifetime.Singleton)
                .MustBeRegistered())
            .ShouldBe("Singleton-registered types must be registered.");
    }

    // ---- The project stratum (GRAMMAR §4.10, §6): the same four placements over a head that is the noun
    //      itself, so `.Named` substitutes it and there is no locative to carry ----

    [Fact]
    public void BareProjectsSubject_IsCapitalizedHead()
    {
        SentenceRenderer.ProjectSubject(Arch.Projects)
            .ShouldBe("Projects");
    }

    [Fact]
    public void NamedProjects_TwoNames_SubstituteTheHeadAsAnOrList()
    {
        // One name reads "project `A`" and several read "projects `A` or `B`" — the head agrees in number
        // with what it names, which is what keeps a two-project law from reading like a one-project one.
        SentenceRenderer.Sentence(Arch.Projects.Named("A", "B")
                .MustLockPackages())
            .ShouldBe("Projects `A` or `B` must lock package restore.");
    }

    [Fact]
    public void PackableAndMatching_StackAsPrefixThenInlineClause()
    {
        // The head-prefix lands in front of the head and the inline clause after it, in exactly the order
        // the type side assembles `.Authored()` and `.InNamespace(...)`.
        SentenceRenderer.Sentence(Arch.Projects.Packable()
                .Matching("Zphil.*")
                .MustNotBePackable())
            .ShouldBe("Packable projects matching `Zphil.*` must not be packable.");
    }

    [Fact]
    public void ProjectExcept_CanonicalizesSentenceFinalAndTheVerbClosesIt()
    {
        // The payload renders in reference position — the same phrase, uncapitalized — and the verb junction
        // closes the parenthetical with a comma, exactly as on the type side.
        SentenceRenderer.Sentence(Arch.Projects.Matching("Zphil.*")
                .Except(Arch.Projects.Named("Zphil.LoadBearing.Cli"))
                .MustNotBePackable())
            .ShouldBe(
                "Projects matching `Zphil.*`, except project `Zphil.LoadBearing.Cli`, must not be packable.");
    }

    [Fact]
    public void ProjectWhere_RendersItsDescriptionSentenceFinal()
    {
        SentenceRenderer.Sentence(Arch.Projects
                .Where(project => project.IsPackable == true, description: "whose name ends in a digit")
                .MustLockPackages())
            .ShouldBe("Projects whose name ends in a digit must lock package restore.");
    }

    [Fact]
    public void MustOnlyTarget_TwoFrameworks_OrJoinsThem()
    {
        SentenceRenderer.Sentence(Arch.Projects.Named("Zphil.LoadBearing")
                .MustOnlyTarget("netstandard2.0", "net8.0"))
            .ShouldBe("Project `Zphil.LoadBearing` must target only `netstandard2.0` or `net8.0`.");
    }

    [Fact]
    public void BareProjectsInReferencePosition_IsTheUncapitalizedPhrase()
    {
        // Reference position differs from subject position by capitalization alone: a project selection has
        // one head and no noun that reads differently as a reference.
        SentenceRenderer.ProjectReference(Arch.Projects.Named("A"))
            .ShouldBe("project `A`");
    }
}

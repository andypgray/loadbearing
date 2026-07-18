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
}
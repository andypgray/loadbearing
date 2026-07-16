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
}
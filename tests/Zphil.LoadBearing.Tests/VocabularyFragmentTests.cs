using Shouldly;
using Xunit;
using Zphil.LoadBearing.Prose;
using Zphil.LoadBearing.Tests.Stubs;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     One authored sentence per modal verb the canonical sample does not exercise (plan
///     Deliverable 2 coverage pins). Constraints are rendered directly through the internal
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
}
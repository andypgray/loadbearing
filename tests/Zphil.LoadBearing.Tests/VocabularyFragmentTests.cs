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
}
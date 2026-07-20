using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The five headline sentence pins from the canonical sample (GRAMMAR §12, plan Deliverable 2).
///     These strings are the spec: each is the deterministic law rendered from a reified rule.
/// </summary>
public class ProseSentenceTests
{
    private static ArchitectureModel BuildCanonical()
    {
        return ArchModelBuilder.Build(new ArchSpec());
    }

    private static string SentenceFor(string id)
    {
        return BuildCanonical().Rules.Single(rule => rule.Id == id).Sentence;
    }

    [Fact]
    public void Enforce_Layering_RendersCollectiveLayerVoice()
    {
        // Bare layer subject → collective voice; layer reference target is not backticked.
        SentenceFor("layering/domain-independent")
            .ShouldBe("The Domain layer must not reference the Web layer.");
    }

    [Fact]
    public void Enforce_InterfaceNaming_RendersOfKindHeadAndPrefix()
    {
        // OfKind substitutes the head plural ("interfaces"); MustHavePrefix → "named `I*`".
        SentenceFor("naming/interfaces")
            .ShouldBe("Interfaces in `MyApp.*` must be named `I*`.");
    }

    [Fact]
    public void Migrate_NoInlineSql_RendersTargetSentenceWithLayerLocativeAndSuffix()
    {
        // The Migrate `to` constraint is what renders as the rule sentence.
        SentenceFor("data-access/no-inline-sql")
            .ShouldBe("Types in the Web layer named `*Controller` must not reference `SqlConnection`.");
    }

    [Fact]
    public void Enforce_HandlerNaming_RendersOpenGenericAndSuffix()
    {
        // Implementing an open generic renders declared type-parameter names: `IHandler<T>`.
        SentenceFor("naming/handlers")
            .ShouldBe("Types implementing `IHandler<T>` must be named `*Handler`.");
    }

    [Fact]
    public void Enforce_DiHandlersViaRegistry_RendersExceptSubjectAndConstructVerb()
    {
        // The DI construction ban (GRAMMAR §5.3): .Except carves the sanctioned root out of the subject
        // (sentence-final "except `HandlerRegistry`"), and the open-generic target renders as
        // "types implementing `IHandler<T>`".
        SentenceFor("di/handlers-via-registry")
            .ShouldBe("Types, except `HandlerRegistry` must not construct types implementing `IHandler<T>`.");
    }

    [Fact]
    public void Enforce_TypeNameLength_RendersMustEscapeHatchDescription()
    {
        // The `.Must(...)` escape-hatch description completes "must …" verbatim.
        SentenceFor("style/type-name-length")
            .ShouldBe("Types in `MyApp.*` must keep type names at or under 40 characters.");
    }

    [Fact]
    public void Migrate_InjectClock_RendersMemberUseSentence()
    {
        // The member-access verb renders each target as a backticked declaring-type dot member,
        // joined with "or" (GRAMMAR §4.5, §6). Validation runs on build, so the members must be real.
        ArchModelBuilder.Build(new InjectClockSpec())
            .Rules.Single(rule => rule.Id == "time/inject-clock").Sentence
            .ShouldBe("Types must not use `DateTime.Now` or `DateTime.UtcNow`.");
    }

    [Fact]
    public void Enforce_AsyncSuffix_RendersMemberSubjectFlagship()
    {
        // Phase 14 acceptance box 1: the flagship member-subject rule (GRAMMAR §4.6, §6). The member
        // subject is "{kind-plural} of {selection-reference}" + the Returning adjective; single anchor.
        ArchModelBuilder.Build(new AsyncSuffixSpec())
            .Rules.Single(rule => rule.Id == "naming/async-suffix").Sentence
            .ShouldBe("Methods of types in `MyApp.Web.*` returning `Task` must be named `*Async`.");
    }

    [Fact]
    public void MustNotUse_ExpressionMintedMethod_RendersParens()
    {
        // The no-churn proof (Phase 15): an expression-minted method anchor renders byte-identically to the
        // typeof form — parens for a method (GRAMMAR §6). The member reifies to the same leaf, so the prose is unchanged.
        ArchModelBuilder.Build(new ExpressionMintedMethodSpec())
            .Rules.Single(rule => rule.Id == "member/no-wait").Sentence
            .ShouldBe("Types must not use `Task.Wait()`.");
    }

    [Fact]
    public void MustNotUse_ExpressionMintedProperty_RendersNoParens()
    {
        // An expression-minted property anchor renders with no parens — identical to the typeof form.
        ArchModelBuilder.Build(new ExpressionMintedPropertySpec())
            .Rules.Single(rule => rule.Id == "member/no-now").Sentence
            .ShouldBe("Types must not use `DateTime.Now`.");
    }

    [Fact]
    public void MustNotUse_VerbStaticVoidMethod_RendersParens()
    {
        // The static-form verb sugar (Phase 16): a bare () => GC.Collect() lambda desugars through
        // arch.Member(() => …) to the same method leaf, so the sentence is byte-identical — parens for a method.
        ArchModelBuilder.Build(new VerbStaticMethodSpec())
            .Rules.Single(rule => rule.Id == "member/verb-collect").Sentence
            .ShouldBe("Types must not use `GC.Collect()`.");
    }

    [Fact]
    public void MustNotUse_VerbStaticProperty_RendersNoParens()
    {
        // The static-form verb sugar over a property renders with no parens — identical to the typeof form.
        ArchModelBuilder.Build(new VerbStaticPropertySpec())
            .Rules.Single(rule => rule.Id == "member/verb-now").Sentence
            .ShouldBe("Types must not use `DateTime.Now`.");
    }

    // The flagship member-ban rule: reads of the ambient clock are banned across all types (Migrate
    // posture; the omitted .Baseline fills its conventional default per GRAMMAR §4.4).
    private sealed class InjectClockSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("time/inject-clock")
                .Migrate(
                    "Code reads the ambient clock directly.",
                    arch.Types.MustNotUse(
                        arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                        arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Wall-clock reads are untestable; inject IClock — ADR-nnn.")
                .Fix("Take IClock in the constructor; see OrderService for the pattern.");
        }
    }

    // The flagship member-subject rule (GRAMMAR §4.6): Web-layer methods returning Task must be *Async.
    // Single-anchor Enforce form, reconciled with PLAN's literal acceptance sentence.
    private sealed class AsyncSuffixSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Selection web = arch.Namespace("MyApp.Web.*");
            arch.Rule("naming/async-suffix")
                .Enforce(web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
                .Because("Async methods are discovered by suffix; agents grep by *Async.");
        }
    }

    // Expression-minted member anchors (Phase 15): the void method form (parens in prose) and the static
    // property form (no parens) — each reifies to the same leaf as the typeof form, so the prose is unchanged.
    private sealed class ExpressionMintedMethodSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("member/no-wait")
                .Enforce(arch.Types.MustNotUse(arch.Member<Task>(t => t.Wait())))
                .Because("Blocking waits deadlock the request thread.");
        }
    }

    private sealed class ExpressionMintedPropertySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("member/no-now")
                .Enforce(arch.Types.MustNotUse(arch.Member(() => DateTime.Now)))
                .Because("Wall-clock reads are untestable.");
        }
    }

    // Static-form verb sugar (Phase 16): the bare () => Type.M lambdas on MustNotUse desugar to the same leaf
    // as arch.Member(() => …), so the rendered sentence is unchanged — parens for the void method, none for
    // the property.
    private sealed class VerbStaticMethodSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("member/verb-collect")
                .Enforce(arch.Types.MustNotUse(() => GC.Collect()))
                .Because("Forced GCs stall the process.");
        }
    }

    private sealed class VerbStaticPropertySpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Rule("member/verb-now")
                .Enforce(arch.Types.MustNotUse(() => DateTime.Now))
                .Because("Wall-clock reads are untestable.");
        }
    }
}
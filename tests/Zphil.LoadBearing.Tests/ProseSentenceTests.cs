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
}
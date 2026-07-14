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
}
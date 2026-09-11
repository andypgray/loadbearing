using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The one composition path <c>render</c> and the card-drift gate share: model (plus optionally the
///     codebase) in, one managed-block body per target file out. Covers what the two callers cannot reach
///     between them — the cost gate's codebase-free shape and its whole truth table, the merge of
///     co-located cards into one file, and the skip arms, which are the case where a declared layer or
///     scope silently produces no card at all. No MSBuild — paths are synthesized through
///     <see cref="CompilationFactory" />.
/// </summary>
public class ContextFileComposerTests
{
    // A Web layer with one bare-subject Enforce rule anchored on it.
    private static readonly IArchitectureSpec WebLayerSpec = ContextFixtures.WebLayer();

    // A Web layer that says what it is for and that no rule anchors on: the rule ranges over the same types
    // through a NamespaceNoun subject, so the layer earns a module-map row and no card.
    private static readonly IArchitectureSpec DescribedUnanchoredLayerSpec = new InlineSpec(arch =>
    {
        arch.Layer("Web", "MyApp.Web.*").Purpose("The HTTP surface: controllers and the views they serve.");
        arch.Rule("layering/web-not-billing")
            .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
            .Because("Web reaches billing only through the facade.");
    });

    // A Billing layer anchored by a rule, over a codebase that holds no billing type.
    private static readonly IArchitectureSpec BillingLayerSpec = new InlineSpec(BillingLayer);

    // A quarantined scope over a namespace no type in the codebase occupies.
    private static readonly IArchitectureSpec AbsentScopeSpec = new InlineSpec(QuarantineBilling);

    // A layer and a quarantined scope over the same namespace, so both cards resolve to one directory.
    private static readonly IArchitectureSpec CoLocatedSpec = CoLocatedWith(QuarantineBilling);

    // The caution twin of AbsentScopeSpec: a posture the cost gate must recognize as something to place.
    private static readonly IArchitectureSpec AbsentCautionSpec = new InlineSpec(CautionBilling);

    // A layer and a cautioned scope over the same namespace, so both cards resolve to one directory.
    private static readonly IArchitectureSpec CoLocatedCautionSpec = CoLocatedWith(CautionBilling);

    private const string SpecName = "MyApp.ArchSpec";

    // The layer half of a co-located fixture, with the scope declared by the caller: the layer-cards-first
    // ordering is a property of the composer, so both postures prove it against one spec shape.
    private static IArchitectureSpec CoLocatedWith(Action<Arch> declareScope)
    {
        return new InlineSpec(arch =>
        {
            BillingLayer(arch);
            declareScope(arch);
        });
    }

    private static void BillingLayer(Arch arch)
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing is downstream of the web layer.");
    }

    private static void QuarantineBilling(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .Dragons("Banker's rounding is line-item level.")
            .Because("Replacement scheduled.");
    }

    private static void CautionBilling(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Caution(arch.Namespace("MyApp.Legacy.Billing.*"))
            .Dragons("Banker's rounding is line-item level.")
            .Because("Every caller depends on the exact rounding.");
    }

    [Fact]
    public void Compose_NoCodebase_ReturnsTheRootFileAlone()
    {
        // The cost gate's shape: a caller that decides extraction is not worth paying passes no codebase
        // and gets the root block, which is a function of the spec alone.
        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(WebLayerSpec), null, "/sln", SpecName);

        composition.Warnings.ShouldBeEmpty();
        ContextFile file = composition.Files.ShouldHaveSingleItem();
        file.Path.ShouldBe(Path.Combine("/sln", "AGENTS.md"));
        // The root block still carries the module map (`### Layers`, a function of the spec); what the
        // missing codebase costs is the per-directory card, whose heading is `## Layer <name>`.
        file.Body.ShouldContain("## Architecture (LoadBearing)");
        file.Body.ShouldContain("### Layers");
        file.Body.ShouldNotContain("## Layer `Web`");
    }

    [Fact]
    public void Compose_AnchoredLayer_PlacesItsCardBesideTheRootFile()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(WebLayerSpec), codebase, "/sln", SpecName);

        composition.Warnings.ShouldBeEmpty();
        composition.Files.Select(file => file.Path)
            .ShouldBe(
                [Path.Combine("/sln", "AGENTS.md"), Path.Combine("src/MyApp.Web", "AGENTS.md")]);

        // A scoped-only file gets the provenance line prepended; the root file carries its own.
        composition.Files[1]
            .Body.ShouldStartWith(AgentContextRenderer.ProvenanceLine(SpecName));
        composition.Files[1]
            .Body.ShouldContain("## Layer `Web`");
    }

    [Fact]
    public void Compose_LayerAndScopeInOneDirectory_MergeIntoOneFileLayerCardFirst()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("src/MyApp.Legacy.Billing/BillingCalculator.cs",
                "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(CoLocatedSpec), codebase, "/sln", SpecName);

        // One file, not two: the second splice would clobber the first, so co-located units merge.
        composition.Files.Count.ShouldBe(2);
        string body = composition.Files[1].Body;
        body.ShouldContain("## Layer `Billing`");
        body.ShouldContain("## Quarantined scope `legacy/billing`");
        body.IndexOf("## Layer `Billing`", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("## Quarantined scope `legacy/billing`", StringComparison.Ordinal));

        // And exactly one provenance line for the merged file, not one per card.
        TextNormalization.Occurrences(body, AgentContextRenderer.ProvenanceLine(SpecName))
            .ShouldBe(1);
    }

    [Fact]
    public void Compose_LayerMatchingNoTypes_WarnsAndWritesNoCard()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The Billing layer is anchored by a rule but matches nothing, so there is no directory to place
        // its card in. The skip has to surface: silently emitting nothing is how a spec stops describing
        // its codebase without anyone noticing.
        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(BillingLayerSpec), codebase, "/sln", SpecName);

        composition.Warnings.ShouldBe(["layer 'Billing' matched no types; no scoped context emitted"]);
        composition.Files.ShouldHaveSingleItem(); // the root file only
    }

    [Fact]
    public void Compose_ScopeMatchingNoTypes_WarnsAndWritesNoCard()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(AbsentScopeSpec), codebase, "/sln", SpecName);

        composition.Warnings.ShouldBe(["scope 'legacy/billing' matched no types; no scoped context emitted"]);
        composition.Files.ShouldHaveSingleItem();
    }

    [Fact]
    public void HasAnythingToPlace_AnchoredLayer_True()
    {
        ContextFileComposer.HasAnythingToPlace(ArchModelBuilder.Build(WebLayerSpec))
            .ShouldBeTrue();
    }

    [Fact]
    public void HasAnythingToPlace_QuarantinedScope_True()
    {
        ContextFileComposer.HasAnythingToPlace(ArchModelBuilder.Build(AbsentScopeSpec))
            .ShouldBeTrue();
    }

    [Fact]
    public void HasAnythingToPlace_CautionedScope_True()
    {
        // The gate reads the scope payload rather than the posture, so a caution earns the extraction cost
        // exactly as a quarantine does — and it must, since its card is the only thing it renders.
        ContextFileComposer.HasAnythingToPlace(ArchModelBuilder.Build(AbsentCautionSpec))
            .ShouldBeTrue();
    }

    [Fact]
    public void Compose_LayerAndCautionInOneDirectory_MergeIntoOneFileLayerCardFirst()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("src/MyApp.Legacy.Billing/BillingCalculator.cs",
                "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(CoLocatedCautionSpec), codebase, "/sln", SpecName);

        // The layer-cards-first ordering is a property of the composer, not of which scope posture it met.
        composition.Files.Count.ShouldBe(2);
        string body = composition.Files[1].Body;
        body.ShouldContain("## Layer `Billing`");
        body.ShouldContain("## Cautioned scope `legacy/billing`");
        body.IndexOf("## Layer `Billing`", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("## Cautioned scope `legacy/billing`", StringComparison.Ordinal));

        // And exactly one provenance line for the merged file, not one per card.
        TextNormalization.Occurrences(body, AgentContextRenderer.ProvenanceLine(SpecName))
            .ShouldBe(1);
    }

    [Fact]
    public void HasAnythingToPlace_DescribedLayerNoRuleAnchorsOn_False()
    {
        // A purpose describes a layer; it never places a card. The gate answers on anchoring alone, so a
        // spec whose only layer is described but unanchored still skips extraction entirely.
        ContextFileComposer.HasAnythingToPlace(ArchModelBuilder.Build(DescribedUnanchoredLayerSpec))
            .ShouldBeFalse();
    }

    [Fact]
    public void Compose_DescribedLayerNoRuleAnchorsOn_RendersTheRowAndPlacesNoCard()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // Even with the codebase paid for, the purpose buys the layer a module-map row and nothing more.
        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(DescribedUnanchoredLayerSpec), codebase, "/sln", SpecName);

        composition.Warnings.ShouldBeEmpty();
        ContextFile file = composition.Files.ShouldHaveSingleItem();
        file.Body.ShouldContain("- **Web** — `MyApp.Web.*`. The HTTP surface: controllers and the views they serve.");
        file.Body.ShouldNotContain("## Layer `Web`");
    }
}

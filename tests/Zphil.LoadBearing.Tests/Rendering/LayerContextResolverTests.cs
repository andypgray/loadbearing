using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Layer placement: a layer earns a "local rules" card iff at least one Enforce or
///     Migrate rule is anchored on it (subject noun head is that layer), and the card lands in the
///     deepest common ancestor directory of the whole layer's types. A refined subject (adjective /
///     <c>Except</c>) keeps its noun head, so it still anchors; a namespace-subject rule over the same
///     types does not; a scope-posture rule never anchors, whichever verb declared the scope (its story is
///     the scope card). A layer
///     matching no types resolves to a null directory with a skip reason. No MSBuild — paths are
///     synthesized through <see cref="CompilationFactory" />.
/// </summary>
public class LayerContextResolverTests
{
    // A Web layer with one bare-subject Enforce rule anchored on it — saying what it is for when given a
    // purpose, which is the sentence the card lede carries.
    private static readonly IArchitectureSpec WebLayerSpec = ContextFixtures.WebLayer();

    private static readonly IArchitectureSpec DescribedWebLayerSpec =
        ContextFixtures.WebLayer("The HTTP surface: controllers and the views they serve.");

    // A Web layer whose only rule has a web.Except(...) subject — a refinement that preserves the noun head.
    private static readonly IArchitectureSpec ExceptRefinedSpec = new InlineSpec(arch =>
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        arch.Rule("layering/web-core-not-legacy")
            .Enforce(web.Except(arch.Namespace("MyApp.Web.Internal.*"))
                .MustNotReference(arch.Namespace("MyApp.Legacy.*")))
            .Because("Public web must not touch legacy.");
    });

    // A Web layer whose only rule ranges over MyApp.Web.* through a NamespaceNoun subject, not the layer.
    private static readonly IArchitectureSpec NamespaceSubjectSpec = new InlineSpec(arch =>
    {
        arch.Layer("Web", "MyApp.Web.*");
        arch.Rule("layering/web-namespace")
            .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("MyApp.Legacy.*")))
            .Because("A namespace-subject rule, deliberately not layer-anchored.");
    });

    // A Web layer whose only rule has a union subject the layer is an operand of — the union owns the
    // subject, so nothing anchors on the layer.
    private static readonly IArchitectureSpec UnionSubjectSpec = new InlineSpec(arch =>
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        arch.Rule("layering/web-or-domain-not-legacy")
            .Enforce(arch.AnyOf(web, arch.Namespace("MyApp.Domain.*"))
                .MustNotReference(arch.Namespace("MyApp.Legacy.*")))
            .Because("Neither the web layer nor the domain touches legacy.");
    });

    // Two layers under one family rule and nothing else: the rule anchors on each cell, and each card
    // lands at its own cell's directory rather than at the subject's head.
    private static readonly IArchitectureSpec FamilySubjectSpec = new InlineSpec(arch =>
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(web, billing).MustNotReferenceEachOther())
            .Because("Neither leaf may grow a dependency on the other.");
    });

    // The same family narrowed by an adjective — a refinement keeps the noun head, family or not.
    private static readonly IArchitectureSpec RefinedFamilySubjectSpec = new InlineSpec(arch =>
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(web, billing).Except(arch.Types.Named("Shared")).MustNotReferenceEachOther())
            .Because("Neither leaf may grow a dependency on the other.");
    });

    // The project form of the same law: it names no layer, so it anchors no card at all.
    private static readonly IArchitectureSpec ProjectFamilySubjectSpec = new InlineSpec(arch =>
    {
        arch.Layer("Web", "MyApp.Web.*");
        arch.Rule("layering/projects-independent")
            .Enforce(arch.Each(arch.Projects.Matching("MyApp.*")).MustNotReferenceEachOther())
            .Because("Each project is its own deployable unit.");
    });

    // A Billing layer with one anchored rule — used to place against a codebase that has no billing types.
    private static readonly IArchitectureSpec BillingLayerSpec = new InlineSpec(arch =>
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing is independent of the web layer.");
    });

    // A hermetically quarantined Billing layer with no other rule — its only layer-anchored subject is the
    // Quarantine containment, which the resolver excludes.
    private static readonly IArchitectureSpec QuarantinedLayerSpec = new InlineSpec(arch =>
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Scope("legacy/billing")
            .Quarantine(billing)
            .Dragons("Banker's rounding is load-bearing.")
            .Because("Replacement scheduled; not worth stabilizing.");
    });

    // A cautioned Billing layer with no other rule. Its one child is a tripwire, which carries no constraint
    // at all — so it anchors nothing whether or not the posture filter were to let it through.
    private static readonly IArchitectureSpec CautionedLayerSpec = new InlineSpec(arch =>
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Scope("legacy/billing")
            .Caution(billing)
            .Dragons("Banker's rounding is load-bearing.")
            .Because("Every caller depends on the exact rounding.");
    });

    [Fact]
    public void Resolve_LayerWithAnchoredRule_PicksDeepestCommonDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"),
            ("src/MyApp.Web/InvoiceController.cs", "namespace MyApp.Web; public class InvoiceController {}"));

        IReadOnlyList<LayerPlacement> placements = LayerContextResolver.Resolve(ArchModelBuilder.Build(WebLayerSpec), codebase);

        LayerPlacement placement = placements.ShouldHaveSingleItem();
        placement.LayerName.ShouldBe("Web");
        placement.Rules.Select(rule => rule.Id)
            .ShouldBe(["layering/web-not-billing"]);
        placement.DirectoryPath.ShouldBe("src/MyApp.Web");
        placement.SkipReason.ShouldBeNull();
        placement.Purpose.ShouldBeNull();
    }

    [Fact]
    public void Resolve_DescribedLayer_CarriesItsPurposeOntoThePlacement()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The purpose rides from the layer definition onto the placement, which is the only route it has
        // into the card lede — the renderer is handed it, never the model.
        LayerPlacement placement = LayerContextResolver.Resolve(ArchModelBuilder.Build(DescribedWebLayerSpec), codebase)[0];

        placement.Purpose.ShouldBe("The HTTP surface: controllers and the views they serve.");
    }

    [Fact]
    public void Resolve_AnchoredViaExcept_StillAnchors()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The rule subject is web.Except(...); the Except refinement keeps the LayerNoun head, so the
        // layer still anchors and the card ranges over the whole layer directory.
        LayerPlacement placement = LayerContextResolver.Resolve(ArchModelBuilder.Build(ExceptRefinedSpec), codebase)[0];

        placement.LayerName.ShouldBe("Web");
        placement.DirectoryPath.ShouldBe("src/MyApp.Web");
    }

    [Fact]
    public void Resolve_NamespaceSubjectRule_LayerGetsNoPlacement()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // A rule whose subject is arch.Namespace("MyApp.Web.*") ranges over the same types as the Web
        // layer, but its noun head is a NamespaceNoun — anchoring is by noun identity, not type set.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(NamespaceSubjectSpec), codebase)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_QuarantinePostureRuleOnLayer_Excluded()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        // The layer is quarantined (its desugared containment subject is layer-anchored) but carries no
        // Enforce/Migrate rule — Quarantine posture is excluded, so the layer earns no card and does not
        // double-emit beside its quarantine card.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(QuarantinedLayerSpec), codebase)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_CautionPostureRuleOnLayer_Excluded()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        // The caution card already covers this directory; a layer card beside it would say the same thing
        // twice in the same file.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(CautionedLayerSpec), codebase)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_LayerMatchingNoTypes_ReturnsNullDirectoryWithSkipReason()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The Billing layer's rule anchors it, but no billing-namespace type exists to place it on.
        LayerPlacement placement = LayerContextResolver.Resolve(ArchModelBuilder.Build(BillingLayerSpec), codebase)[0];

        placement.DirectoryPath.ShouldBeNull();
        placement.SkipReason.ShouldBe("layer 'Billing' matched no types; no scoped context emitted");
    }

    [Fact]
    public void Resolve_FamilyOfLayersSubject_AnchorsEveryCellAtItsOwnDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"),
            ("src/MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        // One rule, two cards: the sentence names every cell, so it reads correctly on each — and the
        // directory is the CELL's, never the subject head's, or both cards would land in one place.
        IReadOnlyList<LayerPlacement> placements = LayerContextResolver.Resolve(
            ArchModelBuilder.Build(FamilySubjectSpec), codebase);

        placements.Select(placement => (placement.LayerName, placement.DirectoryPath))
            .ShouldBe([("Web", "src/MyApp.Web"), ("Billing", "src/MyApp.Legacy.Billing")]);
        placements.ShouldAllBe(placement => placement.Rules.Count == 1);
    }

    [Fact]
    public void Resolve_RefinedFamilySubject_StillAnchorsEveryCell()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"),
            ("src/MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        // An Except on the family produces a RefinedSelection over the same noun, exactly as it does over
        // a bare layer, so anchoring survives it — and each card still ranges over the whole cell.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(RefinedFamilySubjectSpec), codebase)
            .Select(placement => placement.LayerName)
            .ShouldBe(["Web", "Billing"]);
    }

    [Fact]
    public void Resolve_FamilyOfProjectsSubject_AnchorsNothing()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The project form names no layer, so there is no card to place — a bare project noun earns none
        // either, and a family of them is the same answer.
        ArchitectureModel model = ArchModelBuilder.Build(ProjectFamilySubjectSpec);

        LayerContextResolver.Resolve(model, codebase)
            .ShouldBeEmpty();
        LayerContextResolver.HasAnchoredLayers(model)
            .ShouldBeFalse();
    }

    [Fact]
    public void Resolve_UnionSubjectContainingTheLayer_AnchorsNothing()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // A union has no single home directory even when a Layer is one of its operands, so it anchors no
        // scoped card and the rule renders into the root block only (GRAMMAR §6). A union also carries no
        // noun, so anchoring must never be decided by reading one.
        ArchitectureModel model = ArchModelBuilder.Build(UnionSubjectSpec);

        LayerContextResolver.Resolve(model, codebase)
            .ShouldBeEmpty();
        LayerContextResolver.HasAnchoredLayers(model)
            .ShouldBeFalse();
    }

    [Fact]
    public void HasAnchoredLayers_LayerButNoAnchoringRule_False()
    {
        LayerContextResolver.HasAnchoredLayers(ArchModelBuilder.Build(NamespaceSubjectSpec))
            .ShouldBeFalse();
    }

    [Fact]
    public void HasAnchoredLayers_AnchoredRule_True()
    {
        LayerContextResolver.HasAnchoredLayers(ArchModelBuilder.Build(WebLayerSpec))
            .ShouldBeTrue();
    }
}

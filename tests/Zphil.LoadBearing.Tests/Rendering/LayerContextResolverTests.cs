using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Layer placement: a layer earns a "local rules" card iff at least one Enforce or
///     Migrate rule is anchored on it (subject noun head is that layer), and the card lands in the
///     deepest common ancestor directory of the whole layer's types. A refined subject (adjective /
///     <c>Except</c>) keeps its noun head, so it still anchors; a namespace-subject rule over the same
///     types does not; a Quarantine-posture rule never anchors (its story is the quarantine card). A layer
///     matching no types resolves to a null directory with a skip reason. No MSBuild — paths are
///     synthesized through <see cref="CompilationFactory" />.
/// </summary>
public class LayerContextResolverTests
{
    [Fact]
    public void Resolve_LayerWithAnchoredRule_PicksDeepestCommonDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"),
            ("src/MyApp.Web/InvoiceController.cs", "namespace MyApp.Web; public class InvoiceController {}"));

        var placements = LayerContextResolver.Resolve(ArchModelBuilder.Build(new WebLayerSpec()), codebase);

        placements.Count.ShouldBe(1);
        placements[0].LayerName.ShouldBe("Web");
        placements[0].Rules.Select(rule => rule.Id).ShouldBe(["layering/web-not-billing"]);
        placements[0].DirectoryPath.ShouldBe("src/MyApp.Web");
        placements[0].SkipReason.ShouldBeNull();
    }

    [Fact]
    public void Resolve_AnchoredViaExcept_StillAnchors()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The rule subject is web.Except(...); the Except refinement keeps the LayerNoun head, so the
        // layer still anchors and the card ranges over the whole layer directory.
        LayerPlacement placement = LayerContextResolver.Resolve(ArchModelBuilder.Build(new ExceptRefinedSpec()), codebase)[0];

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
        LayerContextResolver.Resolve(ArchModelBuilder.Build(new NamespaceSubjectSpec()), codebase).ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_QuarantinePostureRuleOnLayer_Excluded()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"));

        // The layer is quarantined (its desugared containment subject is layer-anchored) but carries no
        // Enforce/Migrate rule — Quarantine posture is excluded, so the layer earns no card and does not
        // double-emit beside its quarantine card.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(new QuarantinedLayerSpec()), codebase).ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_LayerMatchingNoTypes_ReturnsNullDirectoryWithSkipReason()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // The Billing layer's rule anchors it, but no billing-namespace type exists to place it on.
        LayerPlacement placement = LayerContextResolver.Resolve(ArchModelBuilder.Build(new BillingLayerSpec()), codebase)[0];

        placement.DirectoryPath.ShouldBeNull();
        placement.SkipReason.ShouldBe("layer 'Billing' matched no types; no scoped context emitted");
    }

    [Fact]
    public void Resolve_UnionSubjectContainingTheLayer_AnchorsNothing()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        // A union has no single home directory even when a Layer is one of its operands, so it anchors no
        // scoped card and the rule renders into the root block only (GRAMMAR §6). A union also carries no
        // noun, so anchoring must never be decided by reading one.
        LayerContextResolver.Resolve(ArchModelBuilder.Build(new UnionSubjectSpec()), codebase).ShouldBeEmpty();
        LayerContextResolver.HasAnchoredLayers(ArchModelBuilder.Build(new UnionSubjectSpec())).ShouldBeFalse();
    }

    [Fact]
    public void HasAnchoredLayers_LayerButNoAnchoringRule_False()
    {
        LayerContextResolver.HasAnchoredLayers(ArchModelBuilder.Build(new NamespaceSubjectSpec())).ShouldBeFalse();
    }

    [Fact]
    public void HasAnchoredLayers_AnchoredRule_True()
    {
        LayerContextResolver.HasAnchoredLayers(ArchModelBuilder.Build(new WebLayerSpec())).ShouldBeTrue();
    }

    // A Web layer with one bare-subject Enforce rule anchored on it.
    private sealed class WebLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("layering/web-not-billing")
                .Enforce(web.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
                .Because("Web reaches billing only through the facade.");
        }
    }

    // A Web layer whose only rule has a web.Except(...) subject — a refinement that preserves the noun head.
    private sealed class ExceptRefinedSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("layering/web-core-not-legacy")
                .Enforce(web.Except(arch.Namespace("MyApp.Web.Internal.*")).MustNotReference(arch.Namespace("MyApp.Legacy.*")))
                .Because("Public web must not touch legacy.");
        }
    }

    // A Web layer whose only rule ranges over MyApp.Web.* through a NamespaceNoun subject, not the layer.
    private sealed class NamespaceSubjectSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("layering/web-namespace")
                .Enforce(arch.Namespace("MyApp.Web.*").MustNotReference(arch.Namespace("MyApp.Legacy.*")))
                .Because("A namespace-subject rule, deliberately not layer-anchored.");
        }
    }

    // A Web layer whose only rule has a union subject the layer is an operand of — the union owns the
    // subject, so nothing anchors on the layer.
    private sealed class UnionSubjectSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            arch.Rule("layering/web-or-domain-not-legacy")
                .Enforce(arch.AnyOf(web, arch.Namespace("MyApp.Domain.*"))
                    .MustNotReference(arch.Namespace("MyApp.Legacy.*")))
                .Because("Neither the web layer nor the domain touches legacy.");
        }
    }

    // A Billing layer with one anchored rule — used to place against a codebase that has no billing types.
    private sealed class BillingLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
            arch.Rule("layering/billing-not-web")
                .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
                .Because("Billing is independent of the web layer.");
        }
    }

    // A hermetically quarantined Billing layer with no other rule — its only layer-anchored subject is the
    // Quarantine containment, which the resolver excludes.
    private sealed class QuarantinedLayerSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
            arch.Scope("legacy/billing")
                .Quarantine(billing)
                .Dragons("Banker's rounding is load-bearing.")
                .Because("Replacement scheduled; not worth stabilizing.");
        }
    }
}

using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     The one composition path <c>render</c> and the card-drift gate share: model (plus optionally the
///     codebase) in, one managed-block body per target file out. Covers what the two callers cannot reach
///     between them — the cost gate's codebase-free shape, the merge of co-located cards into one file,
///     and the skip arms, which are the case where a declared layer or scope silently produces no card at
///     all. No MSBuild — paths are synthesized through <see cref="CompilationFactory" />.
/// </summary>
public class ContextFileComposerTests
{
    // A Web layer with one bare-subject Enforce rule anchored on it.
    private static readonly IArchitectureSpec WebLayerSpec = new InlineSpec(arch =>
    {
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        arch.Rule("layering/web-not-billing")
            .Enforce(web.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
            .Because("Web reaches billing only through the facade.");
    });

    // A Billing layer anchored by a rule, over a codebase that holds no billing type.
    private static readonly IArchitectureSpec BillingLayerSpec = new InlineSpec(arch =>
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing is downstream of the web layer.");
    });

    // A quarantined scope over a namespace no type in the codebase occupies.
    private static readonly IArchitectureSpec AbsentScopeSpec = new InlineSpec(arch =>
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .Dragons("Banker's rounding is line-item level.")
            .Because("Replacement scheduled."));

    // A layer and a quarantined scope over the same namespace, so both cards resolve to one directory.
    private static readonly IArchitectureSpec CoLocatedSpec = new InlineSpec(arch =>
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing is downstream of the web layer.");

        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .Dragons("Banker's rounding is line-item level.")
            .Because("Replacement scheduled.");
    });

    private const string SpecName = "MyApp.ArchSpec";

    [Fact]
    public void Compose_NoCodebase_ReturnsTheRootFileAlone()
    {
        // The cost gate's shape: a caller that decides extraction is not worth paying passes no codebase
        // and gets the root block, which is a function of the spec alone.
        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(WebLayerSpec), null, "/sln", SpecName);

        composition.Warnings.ShouldBeEmpty();
        composition.Files.Count.ShouldBe(1);
        composition.Files[0]
            .Path.ShouldBe(Path.Combine("/sln", "AGENTS.md"));
        // The root block still carries the module map (`### Layers`, a function of the spec); what the
        // missing codebase costs is the per-directory card, whose heading is `## Layer <name>`.
        composition.Files[0]
            .Body.ShouldContain("## Architecture (LoadBearing)");
        composition.Files[0]
            .Body.ShouldContain("### Layers");
        composition.Files[0]
            .Body.ShouldNotContain("## Layer `Web`");
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
        Occurrences(body, AgentContextRenderer.ProvenanceLine(SpecName))
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
        composition.Files.Count.ShouldBe(1); // the root file only
    }

    [Fact]
    public void Compose_ScopeMatchingNoTypes_WarnsAndWritesNoCard()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        ContextComposition composition = ContextFileComposer.Compose(
            ArchModelBuilder.Build(AbsentScopeSpec), codebase, "/sln", SpecName);

        composition.Warnings.ShouldBe(["scope 'legacy/billing' matched no types; no scoped context emitted"]);
        composition.Files.Count.ShouldBe(1);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}

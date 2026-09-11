using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The layer definition fragment pin (GRAMMAR §5.1), layer ordering, the optional
///     <c>Purpose</c> trailer the fragment carries after the globs, and how a row spells a definition that
///     is a selection rather than a glob list: a bare noun as its locative without the head, anything else
///     as its reference phrase.
/// </summary>
public class LayerDefinitionTests
{
    [Fact]
    public void Domain_RendersDefinitionFragment()
    {
        LayerDefinition domain = Checker.Canonical
            .Layers.Single(layer => layer.Name == "Domain");

        domain.DefinitionFragment.ShouldBe("**Domain** — `MyApp.Domain.*`. Domain holds the order and customer model.");
        domain.Globs.ShouldBe(["MyApp.Domain.*"]);
        domain.Purpose.ShouldBe("Domain holds the order and customer model.");
    }

    [Fact]
    public void Layers_ExposedInAuthoringOrder()
    {
        Checker.Canonical
            .Layers.Select(layer => layer.Name)
            .ShouldBe(["Domain", "Web"]);
    }

    [Fact]
    public void DescribedLayer_RendersItsPurposeAfterTheDefinition()
    {
        // Prefix-preserving: the fragment a purposeless layer renders, then ". " and the sentence verbatim
        // — the author writes its final period, exactly as Because prose is rendered.
        LayerDefinition domain = Checker.Model(arch =>
                arch.Layer("Domain", "MyApp.Domain.*").Purpose("The quote and rate-card model."))
            .Layers.Single();

        domain.DefinitionFragment.ShouldBe("**Domain** — `MyApp.Domain.*`. The quote and rate-card model.");
        domain.Purpose.ShouldBe("The quote and rate-card model.");
    }

    [Fact]
    public void Purpose_ReturnsTheLayerItWasCalledOn()
    {
        // Which is what makes both authoring forms work: the chained form a discarded layer uses, and the
        // two-statement form that stores the Layer and describes it afterwards.
        Layer? minted = null;
        Layer? returned = null;

        Checker.Model(arch =>
        {
            minted = arch.Layer("Domain", "MyApp.Domain.*");
            returned = minted.Purpose("The quote and rate-card model.");
        });

        returned.ShouldBeSameAs(minted);
    }

    [Fact]
    public void Web_HasNoPurpose_WhenTheSpecGivesItNone()
    {
        Checker.Canonical
            .Layers.Single(layer => layer.Name == "Web")
            .Purpose.ShouldBeNull();
    }

    [Fact]
    public void AProjectDefinition_RendersItsLocativeWithoutTheHead()
    {
        // The row reads its noun the way the glob row reads its own: no "types in" head, because the row
        // is already a definition rather than a sentence about types.
        LayerDefinition core = Checker.Model(arch =>
                arch.Layer("Core", arch.Project("MyApp.Core")).Purpose("Core is the model both readers consume."))
            .Layers.Single();

        core.DefinitionFragment.ShouldBe("**Core** — project `MyApp.Core`. Core is the model both readers consume.");
        core.Globs.ShouldBeEmpty();
    }

    [Fact]
    public void AUnionOfProjectsDefinition_RendersTheirCollapsedLocative()
    {
        Checker.Model(arch =>
                arch.Layer("Kernel", arch.AnyOf(arch.Project("MyApp.Core"), arch.Project("MyApp.Abstractions"))))
            .Layers.Single()
            .DefinitionFragment.ShouldBe("**Kernel** — projects `MyApp.Core` or `MyApp.Abstractions`");
    }

    [Fact]
    public void ARefinementDefinition_RendersItsReferencePhrase()
    {
        // A definition with adjectives has no bare reading, so the row says what the selection says — one
        // register beside the glob row's bare list, and the phrase the sentence renderer already pins.
        Checker.Model(arch =>
            {
                Layer core = arch.Layer("Core", arch.Project("MyApp.Core"));
                arch.Layer("Model", core.InNamespace("MyApp.Core.Model.*"));
            })
            .Layers.Single(layer => layer.Name == "Model")
            .DefinitionFragment.ShouldBe("**Model** — types in the Core layer in `MyApp.Core.Model.*`");
    }

    [Fact]
    public void ANamespaceDefinition_RendersTheGlobRowsBytesAndCarriesTheRegion()
    {
        // The two spellings of one region agree: the row is what the glob form renders, and Globs carries
        // the region the definition names, which is what the drawing collapses on.
        LayerDefinition billing = Checker.Model(arch =>
                arch.Layer("Billing", arch.Namespace("MyApp.Legacy.Billing.*")))
            .Layers.Single();

        billing.DefinitionFragment.ShouldBe("**Billing** — `MyApp.Legacy.Billing.*`");
        billing.Globs.ShouldBe(["MyApp.Legacy.Billing.*"]);
    }

    [Fact]
    public void ATypesDefinitionNarrowedToOneNamespace_CarriesThatRegion()
    {
        // The region rule's second shape: `arch.Types` narrowed by exactly one InNamespace names a
        // namespace region, so Globs carries it and a rule naming that glob collapses onto the layer. Two
        // of them are an intersection and name no region. The drawing has a third shape of its own — see
        // ARefinementDefinition_CarriesNoRegion for why this one must not grow to match it.
        LayerDefinition web = Checker.Model(arch => arch.Layer("Web", arch.Types.InNamespace("MyApp.Web.*")))
            .Layers.Single();

        web.DefinitionFragment.ShouldBe("**Web** — types in `MyApp.Web.*`");
        web.Globs.ShouldBe(["MyApp.Web.*"]);

        Checker.Model(arch => arch.Layer("Both", arch.Types.InNamespace("MyApp.*").InNamespace("MyApp.Web.*")))
            .Layers.Single()
            .Globs.ShouldBeEmpty();
    }

    [Fact]
    public void ARefinementDefinition_CarriesNoRegion()
    {
        // The drawing draws a place-shaped noun narrowed by one InNamespace as that region inside its
        // head, which looks like the same predicate as the one above and must not be folded into it. A
        // named layer keeps its own place and the parent its definition declares; filling Globs here would
        // route it down the classifier's glob arm instead, cost it that parent, and flatten the box it was
        // drawn inside — the Core box on this repository's own fence is the one that would go.
        LayerDefinition model = Checker.Model(arch =>
            {
                Layer core = arch.Layer("Core", arch.Project("MyApp.Core"));
                arch.Layer("Model", core.InNamespace("MyApp.Core.Model.*"));
            })
            .Layers.Last();

        model.Name.ShouldBe("Model");
        model.Globs.ShouldBeEmpty();
    }

    [Fact]
    public void ADefinitionNamingNoRegion_RendersItsReferenceAndCarriesNoGlobs()
    {
        // A registration is a lifetime rather than a location: legal as a definition, and it names no
        // region for the payload or the drawing to stand on.
        LayerDefinition wiring = Checker.Model(arch => arch.Layer("Wiring", arch.Registered(Lifetime.Singleton)))
            .Layers.Single();

        wiring.DefinitionFragment.ShouldBe("**Wiring** — singleton-registered types");
        wiring.Globs.ShouldBeEmpty();
    }
}

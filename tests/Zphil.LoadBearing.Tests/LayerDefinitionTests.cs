using Shouldly;
using Xunit;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The layer definition fragment pin (GRAMMAR §5.1), layer ordering, and the optional
///     <c>Purpose</c> trailer the fragment carries after the globs.
/// </summary>
public class LayerDefinitionTests
{
    private static ArchitectureModel BuildCanonical()
    {
        return ArchModelBuilder.Build(new ArchSpec());
    }

    [Fact]
    public void Domain_RendersDefinitionFragment()
    {
        LayerDefinition domain = BuildCanonical()
            .Layers.Single(layer => layer.Name == "Domain");

        domain.DefinitionFragment.ShouldBe("**Domain** — `MyApp.Domain.*`. Domain holds the order and customer model.");
        domain.Globs.ShouldBe(["MyApp.Domain.*"]);
        domain.Purpose.ShouldBe("Domain holds the order and customer model.");
    }

    [Fact]
    public void Layers_ExposedInAuthoringOrder()
    {
        BuildCanonical()
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
        BuildCanonical()
            .Layers.Single(layer => layer.Name == "Web")
            .Purpose.ShouldBeNull();
    }
}

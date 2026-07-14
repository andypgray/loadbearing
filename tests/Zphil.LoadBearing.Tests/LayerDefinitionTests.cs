using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests;

/// <summary>The layer definition fragment pin (GRAMMAR §5.1) and layer ordering.</summary>
public class LayerDefinitionTests
{
    private static ArchitectureModel BuildCanonical()
    {
        return ArchModelBuilder.Build(new ArchSpec());
    }

    [Fact]
    public void Domain_RendersDefinitionFragment()
    {
        LayerDefinition domain = BuildCanonical().Layers.Single(layer => layer.Name == "Domain");

        domain.DefinitionFragment.ShouldBe("**Domain** — `MyApp.Domain.*`");
        domain.Globs.ShouldBe(["MyApp.Domain.*"]);
    }

    [Fact]
    public void Layers_ExposedInAuthoringOrder()
    {
        BuildCanonical().Layers.Select(layer => layer.Name).ShouldBe(["Domain", "Web"]);
    }
}
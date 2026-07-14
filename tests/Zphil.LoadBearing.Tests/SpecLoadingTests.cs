using System.Reflection;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Discovery;

namespace Zphil.LoadBearing.Tests;

/// <summary>
///     The end-to-end spec-loading proof (plan Deliverable 3): load the fixture DLL — built by a
///     separate csproj with no compile-time visibility here — in an isolated ALC, discover its spec
///     by reflection, assert type identity across the boundary, build the model, and enumerate a
///     known rule. The ALC now lives in the CLI (Phase 3); this test re-targets it. The fixture's
///     path is baked into this assembly's metadata by the build.
/// </summary>
public class SpecLoadingTests
{
    [Fact]
    public void LoadFixtureSpec_ViaAssemblyLoadContext_DiscoversBuildsAndEnumerates()
    {
        string fixturePath = FixtureAssemblyPath();
        var context = new SpecLoadContext(fixturePath);
        try
        {
            Assembly fixture = context.LoadFromAssemblyPath(fixturePath);
            var specs = SpecDiscovery.FindSpecs(fixture);

            specs.ShouldNotBeEmpty();
            // Type identity across the ALC boundary — the shared-contract-to-Default delegation.
            specs[0].ShouldBeAssignableTo<IArchitectureSpec>();

            ArchitectureModel model = ArchModelBuilder.Build(specs);
            ArchRule rule = model.Rules.Single(r => r.Id == "fixture/interfaces");
            rule.Sentence.ShouldBe("Interfaces in `Fixture.*` must be named `I*`.");
        }
        finally
        {
            // Best-effort unload; the phase does not gate on GC timing.
            context.Unload();
        }
    }

    private static string FixtureAssemblyPath()
    {
        string? path = typeof(SpecLoadingTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SpecFixturePath")?.Value;

        path.ShouldNotBeNullOrEmpty();
        return path!;
    }
}
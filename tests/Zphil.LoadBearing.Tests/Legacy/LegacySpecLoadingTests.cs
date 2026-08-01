using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Cli.SpecLoading;
using Zphil.LoadBearing.Discovery;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Cli;

namespace Zphil.LoadBearing.Tests.Legacy;

/// <summary>
///     The cross-platform half of the legacy proof: a spec compiled for <c>net48</c> at the C# 7.3 default,
///     loaded by this .NET 10 host through the same collectible ALC the CLI uses. Modelled on
///     <see cref="SpecLoadingTests" /> and reusing <see cref="SpecLoadContext" /> and
///     <see cref="SpecDiscovery" />, it pins four things the design rests on and one boundary it does not
///     cross.
/// </summary>
public sealed class LegacySpecLoadingTests
{
    private const string ProductAssemblyName = "Zphil.LoadBearing.LegacyProduct";

    [Fact]
    public void LoadLegacySpec_Net48SpecInCollectibleAlc_LoadsBuildsAndResolvesTheProductAppLocally()
    {
        // Arrange
        string specPath = CliRunner.LegacySpecDll;
        var context = new SpecLoadContext(specPath);
        try
        {
            // Act
            Assembly spec = context.LoadFromAssemblyPath(specPath);
            var specs = SpecDiscovery.FindSpecs(spec);
            ArchitectureModel model = ArchModelBuilder.Build(specs);

            // Assert: the fixture really is .NET Framework, not silently retargeted by the build.
            spec.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName
                .ShouldBe(".NETFramework,Version=v4.8");

            // Type identity holds across the ALC boundary — the shared-contract-to-Default delegation.
            specs.ShouldHaveSingleItem().ShouldBeAssignableTo<IArchitectureSpec>();

            // The model builds, and the second sentence comes from a typeof() anchor on a net48 product type.
            model.Rules.Single(rule => rule.Id == "legacy/interfaces").Sentence
                .ShouldBe("Interfaces in `Legacy.*` must be named `I*`.");
            model.Rules.Single(rule => rule.Id == "legacy/gateway-through-interface").Sentence
                .ShouldBe("Types in `Legacy.*` must not construct `BillingGateway`.");

            // The load-bearing finding: a net48 build writes no .deps.json, so the AssemblyDependencyResolver
            // has nothing to consult and falls back to the spec's own output directory — where
            // CopyLocalLockFileAssemblies staged the product DLL. It lands in the spec ALC, not Default.
            File.Exists(Path.ChangeExtension(specPath, ".deps.json")).ShouldBeFalse();
            Assembly product = context.Assemblies.Single(loaded => loaded.GetName().Name == ProductAssemblyName);
            Path.GetDirectoryName(product.Location).ShouldBe(Path.GetDirectoryName(specPath));
            product.Location.ShouldNotBe(CliRunner.LegacyProductDll);
            AssemblyLoadContext.Default.Assemblies
                .Select(loaded => loaded.GetName().Name)
                .ShouldNotContain(ProductAssemblyName);
        }
        finally
        {
            // Best-effort unload; nothing gates on GC timing.
            context.Unload();
        }
    }

    [Fact]
    public void LoadLegacySpec_TypeWithFrameworkOnlyInterface_ThrowsTypeLoadExceptionAndMapsToAUserError()
    {
        // Arrange: the envelope boundary as observed behaviour. Legacy.Product.LegacyHandler implements
        // System.Web.IHttpHandler, which has no counterpart on .NET, so a typeof() anchor on it cannot
        // resolve however the spec project is built.
        string specPath = CliRunner.LegacySpecDll;
        var context = new SpecLoadContext(specPath);
        try
        {
            string productBesideSpec = Path.Combine(
                Path.GetDirectoryName(specPath)!, $"{ProductAssemblyName}.dll");
            Assembly product = context.LoadFromAssemblyPath(productBesideSpec);

            // Act
            var thrown = Should.Throw<TypeLoadException>(() => product.GetType("Legacy.Product.LegacyHandler", true));

            // Assert: the runtime names the unloadable interface, and the CLI's mapping carries that name
            // into a user error rather than letting the raw crash out. This is the companion to the
            // fabricated-exception unit test, which can only reach the unnamed degraded form.
            thrown.TypeName.ShouldBe("System.Web.IHttpHandler");
            UserErrorException mapped = SpecDependencyLoadFailure.Map(thrown, specPath);
            mapped.Message.ShouldStartWith(
                "The spec assembly 'Zphil.LoadBearing.LegacySpec' could not load the type "
                + "'System.Web.IHttpHandler' while running Define().");
        }
        finally
        {
            context.Unload();
        }
    }
}
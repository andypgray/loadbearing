using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Scoped placement: the frozen selection is evaluated in Subject position and its
///     types' declaration sites collapse to a deepest-common-ancestor directory — the right dir for a
///     co-located scope, the containing dir for a single file, the common root for a cross-project
///     scope. A scope matching no types resolves to a null directory with a skip reason. No MSBuild —
///     paths are synthesized through <see cref="CompilationFactory" />.
/// </summary>
public class ScopedContextResolverTests
{
    private static ArchitectureModel Model()
    {
        return ArchModelBuilder.Build(new BillingFreezeSpec());
    }

    [Fact]
    public void Resolve_ScopeCoLocatedInOneDirectory_PicksThatDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"),
            ("MyApp.Legacy.Billing/RoundingMode.cs", "namespace MyApp.Legacy.Billing; public class RoundingMode {}"));

        var placements = ScopedContextResolver.Resolve(Model(), codebase);

        placements.Count.ShouldBe(1);
        placements[0].ScopeId.ShouldBe("legacy/billing");
        placements[0].ContainmentRule.Id.ShouldBe("legacy/billing/containment");
        placements[0].DirectoryPath.ShouldBe("MyApp.Legacy.Billing");
        placements[0].SkipReason.ShouldBeNull();
    }

    [Fact]
    public void Resolve_SingleFileScope_PicksTheContainingDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("src/MyApp.Legacy.Billing/Only.cs", "namespace MyApp.Legacy.Billing; public class Only {}"));

        ScopedContextResolver.Resolve(Model(), codebase)[0].DirectoryPath.ShouldBe("src/MyApp.Legacy.Billing");
    }

    [Fact]
    public void Resolve_ScopeScatteredAcrossProjects_PicksTheCommonRoot()
    {
        CodebaseModel codebase = CodebaseExtractor.ExtractFromCompilations([
            CompilationFactory.Compile("ProjA", ("src/ProjA/Calc.cs", "namespace MyApp.Legacy.Billing; public class Calc {}")),
            CompilationFactory.Compile("ProjB", ("src/ProjB/Facade.cs", "namespace MyApp.Legacy.Billing; public class Facade {}"))
        ]);

        ScopedContextResolver.Resolve(Model(), codebase)[0].DirectoryPath.ShouldBe("src");
    }

    [Fact]
    public void Resolve_AbsoluteBackslashPaths_PreserveRootAndSeparators()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            (@"C:\repo\MyApp.Legacy.Billing\Calc.cs", "namespace MyApp.Legacy.Billing; public class Calc {}"),
            (@"C:\repo\MyApp.Legacy.Billing\Facade.cs", "namespace MyApp.Legacy.Billing; public class Facade {}"));

        ScopedContextResolver.Resolve(Model(), codebase)[0].DirectoryPath.ShouldBe(@"C:\repo\MyApp.Legacy.Billing");
    }

    [Fact]
    public void Resolve_ScopeMatchingNoTypes_ReturnsNullDirectoryWithSkipReason()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Web",
            ("MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

        ScopePlacement placement = ScopedContextResolver.Resolve(Model(), codebase)[0];

        placement.DirectoryPath.ShouldBeNull();
        placement.SkipReason.ShouldBe("scope 'legacy/billing' matched no types; no scoped context emitted");
    }

    // A hermetic frozen scope over the billing namespace (no BoundaryOnlyVia needed to place it).
    private sealed class BillingFreezeSpec : IArchitectureSpec
    {
        public void Define(Arch arch)
        {
            arch.Scope("legacy/billing")
                .Freeze(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding is load-bearing.")
                .Because("Replacement scheduled; not worth stabilizing.");
        }
    }
}

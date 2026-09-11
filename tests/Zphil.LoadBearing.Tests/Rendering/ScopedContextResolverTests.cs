using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Hosting;
using Zphil.LoadBearing.Rendering;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Scoped placement: the scoped selection is evaluated in Subject position and its
///     types' declaration sites collapse to a deepest-common-ancestor directory — the right dir for a
///     co-located scope, the containing dir for a single file, the common root for a cross-project
///     scope. A scope matching no types resolves to a null directory with a skip reason. No MSBuild —
///     paths are synthesized through <see cref="CompilationFactory" />.
/// </summary>
/// <remarks>
///     Each posture places exactly one card per scope, and the single-item assertions are where that is
///     pinned: a quarantine has two children and only its containment bears the card, while a caution's
///     tripwire is both its only child and its card-bearer.
/// </remarks>
public class ScopedContextResolverTests
{
    // A hermetic quarantined scope over the billing namespace (no BoundaryOnlyVia needed to place it).
    private static ArchitectureModel Model()
    {
        return Checker.Model(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding is load-bearing.")
                .Because("Replacement scheduled; not worth stabilizing."));
    }

    // The same shape under the other posture: one child, so the card can only come off the tripwire.
    private static ArchitectureModel CautionModel()
    {
        return Checker.Model(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .Dragons("Argument order is load-bearing.")
                .Because("The helpers are public API for the whole solution."));
    }

    [Fact]
    public void Resolve_ScopeCoLocatedInOneDirectory_PicksThatDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("MyApp.Legacy.Billing/BillingCalculator.cs", "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"),
            ("MyApp.Legacy.Billing/RoundingMode.cs", "namespace MyApp.Legacy.Billing; public class RoundingMode {}"));

        IReadOnlyList<ScopePlacement> placements = ScopedContextResolver.Resolve(Model(), codebase);

        ScopePlacement placement = placements.ShouldHaveSingleItem();
        placement.ScopeId.ShouldBe("legacy/billing");
        placement.Rule.Id.ShouldBe("legacy/billing/containment");
        placement.DirectoryPath.ShouldBe("MyApp.Legacy.Billing");
        placement.SkipReason.ShouldBeNull();
    }

    [Fact]
    public void Resolve_SingleFileScope_PicksTheContainingDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            ("src/MyApp.Legacy.Billing/Only.cs", "namespace MyApp.Legacy.Billing; public class Only {}"));

        ScopedContextResolver.Resolve(Model(), codebase)[0]
            .DirectoryPath.ShouldBe("src/MyApp.Legacy.Billing");
    }

    [Fact]
    public void Resolve_ScopeScatteredAcrossProjects_PicksTheCommonRoot()
    {
        CodebaseModel codebase = CodebaseExtractor.ExtractFromCompilations([
            CompilationFactory.Compile("ProjA", ("src/ProjA/Calc.cs", "namespace MyApp.Legacy.Billing; public class Calc {}")),
            CompilationFactory.Compile("ProjB", ("src/ProjB/Facade.cs", "namespace MyApp.Legacy.Billing; public class Facade {}"))
        ]);

        ScopedContextResolver.Resolve(Model(), codebase)[0]
            .DirectoryPath.ShouldBe("src");
    }

    [Fact]
    public void Resolve_AbsoluteBackslashPaths_PreserveRootAndSeparators()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            (@"C:\repo\MyApp.Legacy.Billing\Calc.cs", "namespace MyApp.Legacy.Billing; public class Calc {}"),
            (@"C:\repo\MyApp.Legacy.Billing\Facade.cs", "namespace MyApp.Legacy.Billing; public class Facade {}"));

        ScopedContextResolver.Resolve(Model(), codebase)[0]
            .DirectoryPath.ShouldBe(@"C:\repo\MyApp.Legacy.Billing");
    }

    [Fact]
    public void Resolve_CautionedScopeOverOneFile_PlacesOneCardOnItsContainingDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Shared",
            ("src/MyApp.Shared/Helpers.cs", "namespace MyApp.Shared; public class Helpers {}"));

        ScopePlacement placement = ScopedContextResolver.Resolve(CautionModel(), codebase)
            .ShouldHaveSingleItem();

        placement.ScopeId.ShouldBe("shared/utilities");
        placement.Rule.Id.ShouldBe("shared/utilities/tripwire");
        placement.DirectoryPath.ShouldBe("src/MyApp.Shared");
        placement.SkipReason.ShouldBeNull();
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
}

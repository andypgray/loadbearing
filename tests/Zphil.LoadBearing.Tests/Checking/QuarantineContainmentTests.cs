using App.Legacy;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Quarantine containment semantics (GRAMMAR §7): the desugared <c>{id}/containment</c>
///     rule evaluated as an ordinary ratcheted rule. Over <c>Sources.Containment</c> —
///     <c>App.Client.User</c> references the interior (<c>Internal</c>, red) and the facade
///     (<c>IFacade</c>, sanctioned green). Uncaptured is a wall of red; a grandfathered inbound edge
///     passes; a new edge from a grandfathered source stays red (pair identity); a stale entry counts
///     without failing; a hermetic scope reds every outside reference. Over
///     <c>Sources.ContainmentRegion</c> the same formula runs with the surface named rather than loaded:
///     a region facade and a consumer sanctioned by name.
/// </summary>
public sealed class QuarantineContainmentTests
{
    private const string ContainmentId = "legacy/quarantined/containment";
    private static readonly CodebaseModel Codebase = Sources.ContainmentModel;

    // Boundary variant: IFacade is the sanctioned surface, resolved by full name via the name-carrier
    // App.Legacy.IFacade (ContainmentFacadeStub.cs).
    private static void BoundaryScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .BoundaryOnlyVia(typeof(IFacade))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    // Hermetic variant: no sanctioned surface, so every inbound reference (including to IFacade) is red.
    private static void HermeticScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    // Region variant: the sanctioned surface is a namespace, so the boundary is spelled with no typeof
    // and the spec assembly never loads the facade.
    private static void RegionScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .BoundaryOnlyVia(arch.Namespace("App.Legacy.Contracts.*"))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    // The field's sanctioned-consumer shape (GRAMMAR §7): the sanctioned surface is a type outside the
    // scope, named rather than anchored, so the formula permits exactly that consumer inward.
    private static void SanctionedConsumerScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .BoundaryOnlyVia(arch.Types.Named("Sanctioned"))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    private static string SymbolId(string fullName)
    {
        return Codebase.Types.Single(t => t.FullName == fullName)
            .SymbolId;
    }

    private static RuleResult RegionContainment(Action<Arch> scope)
    {
        return Checker.Run(Sources.ContainmentRegionModel, BaselineIndex.Empty, scope)
            .ForRule(ContainmentId);
    }

    private static BaselineIndex Index(params BaselineEntry[] entries)
    {
        return Checker.Baselines(ContainmentId, entries);
    }

    private static RuleResult Containment(BaselineIndex baselines, Action<Arch> scope)
    {
        return Checker.Run(Codebase, baselines, scope)
            .ForRule(ContainmentId);
    }

    [Fact]
    public void Uncaptured_InteriorInboundRed_FacadeGreen()
    {
        RuleResult containment = Containment(BaselineIndex.Empty, BoundaryScope);

        // Exhaustive, which is what carries the facade's greenness too: it is absent from a set that names
        // every inbound reference the boundary scope reports.
        containment.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Client.User -> App.Legacy.Internal"]);
        containment.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void GrandfatheredInboundEdge_Passes()
    {
        BaselineIndex baselines = Index(BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Internal")));

        RuleResult containment = Containment(baselines, BoundaryScope);

        containment.ShouldHavePassed();
        containment.BaselineCaptured.ShouldBeTrue();
        containment.ShouldHaveGrandfathered(1);
        containment.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void NewEdgeFromGrandfatheredSource_StaysRed_PairIdentity()
    {
        // Hermetic: User → {IFacade, Internal} are both violations. Grandfather only User → Internal;
        // the User → IFacade edge is a distinct (source, target) pair and stays red (GRAMMAR §4.3).
        BaselineIndex baselines = Index(BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Internal")));

        RuleResult containment = Containment(baselines, HermeticScope);

        containment.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Client.User -> App.Legacy.IFacade"]);
        containment.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void StaleEntry_IsCountedWithoutFailing()
    {
        // Boundary variant → the only violation is User → Internal (grandfathered). The second entry
        // (User → Impl) matches no current violation, so it is stale but the rule still passes.
        BaselineIndex baselines = Index(
            BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Internal")),
            BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Impl")));

        RuleResult containment = Containment(baselines, BoundaryScope);

        containment.ShouldHavePassed();
        containment.ShouldHaveGrandfathered(1);
        containment.ShouldHaveStale(1);
    }

    [Fact]
    public void RegionBoundary_RedsInteriorInbound_WhileTheFacadeRegionIsReachedAndReachesIn()
    {
        RuleResult containment = RegionContainment(RegionScope);

        // Exhaustive, which is what carries the two greens: Caller → Gateway (the facade region is not the
        // containment subject) and Gateway → Internal (the facade's own calls inward) are both absent.
        containment.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "App.Client.Other -> App.Legacy.Internal",
                "App.Client.Sanctioned -> App.Legacy.Internal"
            ]);
    }

    [Fact]
    public void SanctionedConsumerBoundary_AdmitsTheNamedOutsider_AndRedsEveryOther()
    {
        RuleResult containment = RegionContainment(SanctionedConsumerScope);

        // Sanctioned → Internal is absent: the sanctioned consumer lives outside the scope, which the
        // formula is indifferent to. Other differs from it by its name alone, and is red.
        containment.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "App.Client.Caller -> App.Legacy.Contracts.Gateway",
                "App.Client.Other -> App.Legacy.Internal"
            ]);
    }

    [Fact]
    public void HermeticScope_RedsEveryOutsideReference()
    {
        RuleResult containment = Containment(BaselineIndex.Empty, HermeticScope);

        containment.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "App.Client.User -> App.Legacy.IFacade",
                "App.Client.User -> App.Legacy.Internal"
            ]);
    }
}

using App.Legacy;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Freeze containment semantics (GRAMMAR §7): the desugared <c>{id}/containment</c>
///     rule evaluated as an ordinary ratcheted rule. Over <c>Sources.Containment</c> —
///     <c>App.Client.User</c> references the interior (<c>Internal</c>, red) and the facade
///     (<c>IFacade</c>, sanctioned green). Uncaptured is a wall of red; a grandfathered inbound edge
///     passes; a new edge from a grandfathered source stays red (pair identity); a stale entry counts
///     without failing; a hermetic scope reds every outside reference.
/// </summary>
public sealed class FreezeContainmentTests
{
    private const string ContainmentId = "legacy/frozen/containment";
    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(Sources.Containment);

    // Boundary variant: IFacade is the sanctioned surface, resolved by full name via the name-carrier
    // App.Legacy.IFacade (ContainmentFacadeStub.cs).
    private static void BoundaryScope(Arch arch)
    {
        arch.Scope("legacy/frozen")
            .Freeze(arch.Namespace("App.Legacy.*"))
            .BoundaryOnlyVia(typeof(IFacade))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    // Hermetic variant: no sanctioned surface, so every inbound reference (including to IFacade) is red.
    private static void HermeticScope(Arch arch)
    {
        arch.Scope("legacy/frozen")
            .Freeze(arch.Namespace("App.Legacy.*"))
            .Dragons("Internal is load-bearing.")
            .Because("Replacement scheduled.");
    }

    private static string SymbolId(string fullName)
    {
        return Codebase.Types.Single(t => t.FullName == fullName).SymbolId;
    }

    private static BaselineIndex Index(params BaselineEntry[] entries)
    {
        return new BaselineIndex(
            new Dictionary<string, RuleBaseline>(StringComparer.Ordinal) { [ContainmentId] = new(entries) });
    }

    private static RuleResult Containment(BaselineIndex baselines, Action<Arch> scope)
    {
        return Checker.Run(Codebase, baselines, null, scope).ForRule(ContainmentId);
    }

    [Fact]
    public void Uncaptured_InteriorInboundRed_FacadeGreen()
    {
        RuleResult containment = Containment(BaselineIndex.Empty, BoundaryScope);

        containment.Status.ShouldBe(RuleStatus.Failed);
        containment.ReferencePairs().ShouldContain("App.Client.User -> App.Legacy.Internal");
        containment.ReferencePairs().ShouldNotContain("App.Client.User -> App.Legacy.IFacade");
        containment.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void GrandfatheredInboundEdge_Passes()
    {
        BaselineIndex baselines = Index(BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Internal")));

        RuleResult containment = Containment(baselines, BoundaryScope);

        containment.Status.ShouldBe(RuleStatus.Passed);
        containment.BaselineCaptured.ShouldBeTrue();
        containment.Grandfathered.Count.ShouldBe(1);
        containment.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void NewEdgeFromGrandfatheredSource_StaysRed_PairIdentity()
    {
        // Hermetic: User → {IFacade, Internal} are both violations. Grandfather only User → Internal;
        // the User → IFacade edge is a distinct (source, target) pair and stays red (GRAMMAR §4.3).
        BaselineIndex baselines = Index(BaselineEntry.ForEdge(SymbolId("App.Client.User"), SymbolId("App.Legacy.Internal")));

        RuleResult containment = Containment(baselines, HermeticScope);

        containment.Status.ShouldBe(RuleStatus.Failed);
        containment.ReferencePairs().ShouldBe(["App.Client.User -> App.Legacy.IFacade"]);
        containment.Grandfathered.Count.ShouldBe(1);
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

        containment.Status.ShouldBe(RuleStatus.Passed);
        containment.Grandfathered.Count.ShouldBe(1);
        containment.StaleBaselineEntries.ShouldBe(1);
    }

    [Fact]
    public void HermeticScope_RedsEveryOutsideReference()
    {
        RuleResult containment = Containment(BaselineIndex.Empty, HermeticScope);

        containment.Status.ShouldBe(RuleStatus.Failed);
        containment.ReferencePairs().ShouldBe(
        [
            "App.Client.User -> App.Legacy.IFacade",
            "App.Client.User -> App.Legacy.Internal"
        ]);
    }
}
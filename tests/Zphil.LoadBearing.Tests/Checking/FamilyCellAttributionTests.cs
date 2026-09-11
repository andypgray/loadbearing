using Shouldly;
using Xunit;
using Zphil.LoadBearing.Baselines;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Which cell a family verb's violation is attributed to (<see cref="Violation.Cell" />) — the label the
///     <c>status</c> burndown groups a ratcheted family rule's remaining debt by. All four cell-reading verbs
///     label the cell whose law was broken, which is the cell the per-cell walk was on, never anything read
///     off the edge.
/// </summary>
/// <remarks>
///     The two rules agree for the three outbound verbs, where the subject sits at the edge's source. They
///     part for the inbound leaf, where the subject sits at the edge's TARGET — and there the referencing
///     type is regularly outside the family altogether, so source-cell attribution would scatter the debt
///     into a bucket no cell answers for. That divergence is what
///     <see cref="MustOnlyBeReferencedByItself_LabelsTheReferencedCellNotTheReferencingOne" /> pins.
/// </remarks>
public sealed class FamilyCellAttributionTests
{
    // Two modules pointing at each other — so one bed carries a cross-cell pair in each direction and a
    // circle over the two cells — plus a consumer in neither, whose edge reaches into a cell it does not
    // belong to. Caller is the discriminator: it has no cell of its own to be attributed to.
    private const string Modules = """
                                   namespace Ops.Dispatch
                                   {
                                       public class DispatchEngine { public Ops.Tracking.TrackingEngine Cross; }
                                       public class DispatchGate {}
                                   }
                                   namespace Ops.Tracking { public class TrackingEngine { public Ops.Dispatch.DispatchGate Back; } }
                                   namespace Ops.Client { public class Caller { public Ops.Tracking.TrackingEngine Interior; } }
                                   """;

    // A type two projects compile from one linked file, reaching a type only a third declares: the shape
    // that puts one pair in front of two cells at once, which is what the report's dedup has to settle.
    private const string Bridge = "namespace Shared { public class Bridge { public Api.Gate G; } }";

    private const string Gate = "namespace Api { public class Gate {} }";

    private static readonly CodebaseModel ModulesModel = CompilationFactory.Extract(Modules);

    private static readonly CodebaseModel OverlappingCells = Overlapping();

    [Fact]
    public void MustNotReferenceEachOther_LabelsThePairWithTheCellItsWalkWasOn()
    {
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/independent")
                    .Enforce(arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        Labelled(result)
            .ShouldBe(
            [
                "Ops.Dispatch.DispatchEngine -> Ops.Tracking.TrackingEngine [Dispatch]",
                "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchGate [Tracking]"
            ]);
    }

    [Fact]
    public void MustOnlyReference_LabelsThePairWithTheCellWhoseAllowListItFailed()
    {
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/client-only")
                    .Enforce(arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                        .MustOnlyReference(arch.Layer("Client", "Ops.Client.*")))
                    .Because("b"))
            .Single();

        // The outbound allow-list reaches the same two pairs the cross-cell ban does, and labels them the
        // same way: the walk is per cell either way, and the subject sits at the source under both.
        Labelled(result)
            .ShouldBe(
            [
                "Ops.Dispatch.DispatchEngine -> Ops.Tracking.TrackingEngine [Dispatch]",
                "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchGate [Tracking]"
            ]);
    }

    [Fact]
    public void MustOnlyBeReferencedByItself_LabelsTheReferencedCellNotTheReferencingOne()
    {
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/internals")
                    .Enforce(arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                        .MustOnlyBeReferencedByItself())
                    .Because("b"))
            .Single();

        // The whole reason the loop index is the label and the edge is not. Under source-cell attribution
        // these three would read Tracking, Dispatch and — for Caller, which belongs to no cell — nothing at
        // all, so a third of this rule's debt would sit in a bucket the burndown could not name.
        Labelled(result)
            .ShouldBe(
            [
                "Ops.Client.Caller -> Ops.Tracking.TrackingEngine [Tracking]",
                "Ops.Dispatch.DispatchEngine -> Ops.Tracking.TrackingEngine [Tracking]",
                "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchGate [Dispatch]"
            ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_LabelsThePairWithTheCellItsArrowLeaves()
    {
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/acyclic")
                    .Enforce(arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                        .MustNotHaveCircularReferences())
                    .Because("b"))
            .Single();

        // Both arrows of the circle red, and each is attributed to the cell it leaves — so a burndown over
        // this rule says which end of the circle is carrying the debt.
        Labelled(result)
            .ShouldBe(
            [
                "Ops.Dispatch.DispatchEngine -> Ops.Tracking.TrackingEngine [Dispatch]",
                "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchGate [Tracking]"
            ]);
    }

    [Fact]
    public void PlainSubject_LabelsNothing()
    {
        // The control that keeps the label a family fact: the same edge, under a rule with no cells, carries
        // no cell — so every renderer that does not ask sees exactly the violation it saw before.
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/dispatch-independent")
                    .Enforce(arch.Namespace("Ops.Dispatch.*").MustNotReference(arch.Namespace("Ops.Tracking.*")))
                    .Because("b"))
            .Single();

        Labelled(result)
            .ShouldBe(["Ops.Dispatch.DispatchEngine -> Ops.Tracking.TrackingEngine [none]"]);
    }

    [Fact]
    public void OverlappingProjectCells_TakeTheFirstCellInDeclarationOrder()
    {
        // Alpha and Beta each compile the linked Bridge, so the one pair Bridge → Gate is judged under both
        // cells and reported once (GRAMMAR §4.3). The surviving copy is the first walked, and the walk is in
        // the family's declaration order — projects by name — so the label is Alpha rather than dictionary
        // luck. Without that the same run could attribute one pair two ways.
        RuleResult result = Checker.Run(OverlappingCells, arch =>
                arch.Rule("projects/independent")
                    .Enforce(arch.Each(arch.Projects).MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        Labelled(result)
            .ShouldBe(["Shared.Bridge -> Api.Gate [Alpha]"]);
    }

    [Fact]
    public void Grandfathered_KeepTheLabelTheyWereMintedWith()
    {
        // The ratchet partitions the very instances the evaluator minted, so the label rides into the
        // tolerated list untouched — which is the only reason a burndown can group by it at all.
        BaselineIndex index = Checker.Baselines(
            "modules/internals",
            BaselineEntry.ForEdge("T:Ops.Client.Caller", "T:Ops.Tracking.TrackingEngine"),
            BaselineEntry.ForEdge("T:Ops.Tracking.TrackingEngine", "T:Ops.Dispatch.DispatchGate"));

        RuleResult result = Checker.Run(ModulesModel, index, arch =>
                arch.Rule("modules/internals")
                    .Migrate(
                        "old",
                        arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                            .MustOnlyBeReferencedByItself())
                    .Because("b"))
            .Single();

        Labelled(result.Grandfathered)
            .ShouldBe(
            [
                "Ops.Client.Caller -> Ops.Tracking.TrackingEngine [Tracking]",
                "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchGate [Dispatch]"
            ]);
    }

    private static IReadOnlyList<string> Labelled(RuleResult result)
    {
        return Labelled(result.Violations);
    }

    // Every violation with its label spelled out, so a red names the whole attribution rather than the one
    // pair Shouldly happened to reach first.
    private static IReadOnlyList<string> Labelled(IReadOnlyList<Violation> violations)
    {
        return violations
            .Select(violation =>
                $"{violation.Source!.FullName} -> {violation.Target!.FullName} [{violation.Cell ?? "none"}]")
            .ToList();
    }

    private static CodebaseModel Overlapping()
    {
        CompilationInput api = CompilationFactory.Compile("Api", ("Gate.cs", Gate));
        CompilationInput alpha = CompilationFactory.CompileReferencing(
            "Alpha", api.Compilation, "Api", ("Shared/Bridge.cs", Bridge));
        CompilationInput beta = CompilationFactory.CompileReferencing(
            "Beta", api.Compilation, "Api", ("Shared/Bridge.cs", Bridge));

        return CodebaseExtractor.ExtractFromCompilations([api, alpha, beta]);
    }
}

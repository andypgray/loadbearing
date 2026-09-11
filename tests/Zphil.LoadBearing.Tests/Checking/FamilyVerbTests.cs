using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The family noun over the fast path (GRAMMAR §5.1, §5.3): the per-cell self the four
///     <c>MustOnly*</c> reference verbs read, the two leaf verbs, and the partition discipline.
/// </summary>
/// <remarks>
///     One bed for the cross-cell laws — two plugin cells, a shared host neither cell owns, and a region
///     outside every cell, which is what separates the cross-cell ban from the allow-list: an edge into
///     <c>Outside</c> is green under the first and red under the second. A second bed carries the
///     modular-monolith shape the inbound leaf exists for: each module's <c>Contracts</c> cone is carved
///     out of the subject and still counts as that module's self.
/// </remarks>
public sealed class FamilyVerbTests
{
    // Two cells, a host outside them both, and a free region outside everything. Alpha reaches all three
    // — its own cell, the host, and Outside — while BetaHelper reaches back into the other cell, so both
    // directions of the cross-cell ban are readable off one run.
    private const string Plugins = """
                                   namespace Host { public class Kernel {} }
                                   namespace Outside { public class Free {} }
                                   namespace Plugin.A
                                   {
                                       public class Alpha { public Host.Kernel Kernel; public AlphaHelper Helper; public Plugin.B.Beta Cross; }
                                       public class AlphaHelper { public Outside.Free Free; }
                                   }
                                   namespace Plugin.B
                                   {
                                       public class Beta {}
                                       public class BetaHelper { public Plugin.A.Alpha Back; }
                                   }
                                   """;

    // The Operations shape: two modules, each a public Contracts cone over its internals, plus a consumer
    // outside both. DispatchGateway reaches its own module's internals (sanctioned by the cell as
    // declared, though the subject's Except removed it); TrackingEngine reaches Dispatch's internals and
    // Caller reaches Tracking's, which is the law's whole point.
    private const string Modules = """
                                   namespace Ops.Dispatch.Contracts { public class DispatchGateway { public Ops.Dispatch.DispatchEngine Engine; } }
                                   namespace Ops.Dispatch { public class DispatchEngine {} }
                                   namespace Ops.Tracking.Contracts { public class TrackingGateway {} }
                                   namespace Ops.Tracking { public class TrackingEngine { public Ops.Dispatch.DispatchEngine Foreign; } }
                                   namespace Ops.Client
                                   {
                                       public class Caller
                                       {
                                           public Ops.Dispatch.Contracts.DispatchGateway Sanctioned;
                                           public Ops.Tracking.TrackingEngine Interior;
                                       }
                                   }
                                   """;

    private static readonly CodebaseModel PluginsModel = CompilationFactory.Extract(Plugins);

    private static readonly CodebaseModel ModulesModel = CompilationFactory.Extract(Modules);

    [Fact]
    public void MustNotReferenceEachOther_CrossCellEdges_AreRedInBothDirections()
    {
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/independent")
                    .Enforce(arch.Each(arch.Layer("A", "Plugin.A.*"), arch.Layer("B", "Plugin.B.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        // Exhaustive: the two cross-cell edges and nothing else. Alpha's own-cell edge (→ AlphaHelper),
        // its host edge and AlphaHelper's Outside edge are all green — an "other" is a cell, never merely
        // somewhere else.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference,
        [
            "Plugin.A.Alpha -> Plugin.B.Beta",
            "Plugin.B.BetaHelper -> Plugin.A.Alpha"
        ]);
    }

    [Fact]
    public void MustNotReferenceEachOther_OneCellFamily_PassesVacuouslyAndReadsAsTheCell()
    {
        // A loop that built a family of one has said what the cell says: no other cell exists, so the ban
        // has nothing to forbid, and the sentence is the cell's own (GRAMMAR §5.1's identity).
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/independent")
                    .Enforce(arch.Each(arch.Layer("A", "Plugin.A.*")).MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
        result.Rule.Sentence.ShouldBe("The A layer must not reference the others.");
    }

    [Fact]
    public void MustOnlyReference_OverAFamily_AllowsEachCellItsOwnMembersAndTheNamedTarget()
    {
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/host-only")
                    .Enforce(arch.Each(arch.Layer("A", "Plugin.A.*"), arch.Layer("B", "Plugin.B.*"))
                        .MustOnlyReference(arch.Layer("Host", "Host.*")))
                    .Because("b"))
            .Single();

        // Self is the cell, so Alpha → AlphaHelper is allowed while Alpha → Beta is not; the written
        // operand allows the host from either cell. Outside is allowed by nothing, which is what parts
        // this verb from the cross-cell ban over the same subject.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference,
        [
            "Plugin.A.Alpha -> Plugin.B.Beta",
            "Plugin.A.AlphaHelper -> Outside.Free",
            "Plugin.B.BetaHelper -> Plugin.A.Alpha"
        ]);
    }

    [Fact]
    public void MustOnlyBeReferencedByItself_OverAFamilyWithExcept_TreatsTheCellAsDeclaredAsSelf()
    {
        RuleResult result = Checker.Run(ModulesModel, arch =>
                arch.Rule("modules/internals")
                    .Enforce(arch.Each(arch.Layer("Dispatch", "Ops.Dispatch.*"), arch.Layer("Tracking", "Ops.Tracking.*"))
                        .Except(arch.Namespace("Ops.Dispatch.Contracts.*"), arch.Namespace("Ops.Tracking.Contracts.*"))
                        .MustOnlyBeReferencedByItself())
                    .Because("b"))
            .Single();

        // DispatchGateway → DispatchEngine is green although the Except removed the gateway from the
        // subject: on a family "self" is the cell as DECLARED, which is the whole module. The two reds are
        // the cross-module edge and the outside consumer reaching an interior; Caller → DispatchGateway is
        // never judged, the gateway having left the subject.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference,
        [
            "Ops.Tracking.TrackingEngine -> Ops.Dispatch.DispatchEngine",
            "Ops.Client.Caller -> Ops.Tracking.TrackingEngine"
        ]);
        result.Rule.Sentence.ShouldBe(
            "Types in each of the Dispatch and Tracking layers, except types in `Ops.Dispatch.Contracts.*` "
            + "or `Ops.Tracking.Contracts.*`, must be referenced only by their own layer.");
    }

    [Fact]
    public void MustOnlyBeReferencedByItself_OnAPlainSubject_AllowsOnlyTheRefinedSubject()
    {
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/hermetic")
                    .Enforce(arch.Namespace("Plugin.A.*").MustOnlyBeReferencedByItself())
                    .Because("b"))
            .Single();

        // The inbound leaf over a plain selection is a hermetic set: the intra-subject edge
        // (Alpha → AlphaHelper) is allowed, the one edge in from outside is not.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["Plugin.B.BetaHelper -> Plugin.A.Alpha"]);
        result.Rule.Sentence.ShouldBe("Types in `Plugin.A.*` must be referenced only by themselves.");
    }

    [Fact]
    public void MustNotReferenceEachOther_ATypeInTwoLayerCells_IsARuleError()
    {
        // Fail closed: a type in two cells has two selves and the rule cannot say which it meant. The
        // report names the type and both cells, because the fix is to redraw one of them.
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/overlapping")
                    .Enforce(arch.Each(arch.Layer("Wide", "Plugin.*"), arch.Layer("A", "Plugin.A.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.RuleError,
            "Type `Plugin.A.Alpha` sits in both the Wide and A cells of the family; "
            + "a family's cells must not overlap.");
    }

    [Fact]
    public void MustNotReferenceEachOther_TheSameLayerListedTwice_IsARuleErrorNamingTheSlip()
    {
        // The degenerate overlap, and it gets its own sentence: nothing about the codebase is wrong, the
        // family lists one cell twice.
        RuleResult result = Checker.Run(PluginsModel, arch =>
            {
                Layer cell = arch.Layer("A", "Plugin.A.*");
                arch.Rule("plugins/duplicated")
                    .Enforce(arch.Each(cell, cell).MustNotReferenceEachOther())
                    .Because("b");
            })
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.RuleError, "The A layer is listed twice in the family.");
    }

    [Fact]
    public void MustNotReferenceEachOther_ALayerCellMatchingNothing_FailsNamingThatCell()
    {
        // A family sharpens the empty-subject default per cell exactly as a union does per operand
        // (GRAMMAR §9): a typo'd cell is never masked by its siblings.
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/ghost")
                    .Enforce(arch.Each(arch.Layer("A", "Plugin.A.*"), arch.Layer("Ghost", "Nowhere.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject,
            ConstraintEvaluator.EmptyOperandMessage("the Ghost layer"));
    }

    [Fact]
    public void MustNotReferenceEachOther_AProjectCellDeclaringNoType_FailsNamingThatProject()
    {
        CodebaseModel scene = ProjectFacts.Solution(ProjectFacts.Project("Empty"));

        RuleResult result = Checker.Run(scene, arch =>
                arch.Rule("plugins/empty-project")
                    .Enforce(arch.Each(arch.Projects).MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject,
            ConstraintEvaluator.EmptyOperandMessage("types in project `Empty`"));
    }

    [Fact]
    public void MustNotReferenceEachOther_AProjectFormNamingNoProject_FailsWithThePlainEmptySubject()
    {
        // There is no cell to name here, so the ordinary empty-subject verdict is the honest one.
        RuleResult result = Checker.Run(PluginsModel, arch =>
                arch.Rule("plugins/no-projects")
                    .Enforce(arch.Each(arch.Projects.Named("Nowhere")).MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustNotReferenceEachOther_OverAProjectFamily_JudgesEachInstanceAtItsDeclarer()
    {
        // The linked-file bed (GRAMMAR §4.1): Shared.Widget and Shared.WidgetPart are one node each,
        // compiled into Core, Tool and Stub. Every instance of Widget → WidgetPart is intra-copy, so the
        // edge is green in a family whose cells are those very projects; Client compiles no copy, so its
        // reference into Core's declaration crosses a cell boundary and reds.
        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("projects/independent")
                    .Enforce(arch.Each(arch.Projects).MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["Client.Consumer -> Shared.Widget"]);
        result.Rule.Sentence.ShouldBe("Each of the projects must not reference the others.");
    }
}

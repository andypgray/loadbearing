using Shared;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Edge attribution where one source file compiles into several projects (GRAMMAR §4.1). Membership
///     alone decides every position that is not an edge end; at an edge end a project-headed operand asks
///     which project the reference belongs to. An edge whose source declares the target itself is
///     intra-project — it counts against the compiling project alone — and every other edge counts against
///     the first declarer, whose facts the node carries. Heads that are not projects stay
///     attribution-insensitive, an edge's <em>source</em> position stays plain membership, and
///     <c>MustOnly*</c> stays strict.
/// </summary>
/// <remarks>
///     The bed is <see cref="MultiplyDeclaredCodebase" />, described in full in its own remarks;
///     <see cref="MultiplyDeclaredMembershipTests" /> carries the membership half over the same model. Every
///     row here anchors its subject with a namespace where the claim is about the target end, so that the
///     outward edge of the shared type itself — which every project-headed operand admits, since the shared
///     type is declared by all three — cannot ride into a verdict the row is not making.
/// </remarks>
public sealed class MultiplyDeclaredAttributionTests
{
    [Fact]
    public void MustNotReference_ProjectReferencesItsOwnCompiledInCopy_WinnerAnchoredTargetStaysGreen()
    {
        // Command and Widget are both declared by Tool, so Command's reference is intra-project and belongs
        // to Tool. A ban anchored on Core forbids Core's declaration, and this reference never reaches it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-core")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Project("Core")))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_CrossProjectReferenceIntoTheWinner_WinnerAnchoredTargetIsRed()
    {
        // Client declares no copy of the shared file, so its reference crosses a project boundary and is
        // attributed to the declarer whose facts the node carries. The ban anchored on Core catches it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-core")
                    .Enforce(arch.Namespace("Client.*").MustNotReference(arch.Project("Core")))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Client.Consumer", "Shared.Widget");
    }

    [Fact]
    public void MustNotReference_CrossProjectReferenceIntoTheWinner_LoserAnchoredTargetStaysGreen()
    {
        // The same edge under a ban anchored on Tool. Tool declares the type but did not compile this
        // reference, so the edge is none of Tool's business and the rule holds.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-tool")
                    .Enforce(arch.Namespace("Client.*").MustNotReference(arch.Project("Tool")))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_IntraCopyEdge_TypeofTargetStillReds()
    {
        // A typeof target names the type rather than a project, so it asks nothing about attribution: the
        // very edge Project("Core") let through in the first row is forbidden here.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-widget")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(typeof(Widget)))
                    .Because("The command surface is built on parts, not on whole widgets."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotReference_IntraCopyEdge_NamespaceTargetStillReds()
    {
        // The second attribution-insensitive head, and the one a rule author is likelier to write: a
        // namespace operand forbids the shared namespace in every project that compiles it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-shared")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Namespace("Shared.*")))
                    .Because("The command surface is built on parts, not on whole widgets."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotConstruct_ProjectConstructsItsOwnCompiledInCopy_WinnerAnchoredTargetStaysGreen()
    {
        // The construction verb reads the same attribution off the same endpoint pair: Make()'s `new` is
        // Tool's own, so a ban anchored on Core does not reach it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("di/tool-not-core")
                    .Enforce(arch.Namespace("Tool.*").MustNotConstruct(arch.Project("Core")))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();

        // The same `new` under a typeof ban, so the green above is attribution rather than a missing edge.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("di/tool-not-widget")
                    .Enforce(arch.Namespace("Tool.*").MustNotConstruct(typeof(Widget)))
                    .Because("Widgets come from the factory."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Construction, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotBeReferencedBy_WinnerAnchoredSubject_DoesNotCountALosersOwnCopyEdge()
    {
        // The inbound verb puts the subject at the edge's target end, so the same rule decides which edges
        // it counts. Anchored on Core, the subject does not count Tool's reference into Tool's own copy —
        // and Tool's namespace is the only forbidden source, so the rule has nothing left to fail on.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-not-from-tool")
                    .Enforce(arch.Project("Core").MustNotBeReferencedBy(arch.Namespace("Tool.*")))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotBeReferencedBy_LoserAnchoredSubject_CountsThatLosersOwnCopyEdge()
    {
        // The same edge, the same forbidden source, and the subject moved to the declarer that compiled it.
        // Tool's copy is the one Command reached, so anchored on Tool the reference counts and the rule reds.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-from-tool")
                    .Enforce(arch.Project("Tool").MustNotBeReferencedBy(arch.Namespace("Tool.*")))
                    .Because("A tool's types are reached through its command surface."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotBeReferencedBy_TypeofSubject_CountsEveryEdgeWhoeverCompiledIt()
    {
        // A typeof subject asks nothing about attribution either, so every inbound reference counts: the
        // winner's own (User), a loser's into its own copy (Command), and the cross-project one (Consumer).
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/widget-is-sealed-off")
                    .Enforce(arch.Type(typeof(Widget)).MustNotBeReferencedBy(arch.Types))
                    .Because("Widgets are reached through their parts."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, [
                "Client.Consumer -> Shared.Widget",
                "Core.User -> Shared.Widget",
                "Tool.Command -> Shared.Widget"
            ]);
    }

    [Fact]
    public void MustNotBeReferencedBy_SourceOperandNamingALoserDeclarer_ReachesEdgesFromTheSharedType()
    {
        // An edge's source belongs to every declarer, because each one's compilation genuinely makes the
        // reference. So Project("Tool") in source position reaches Command's edge and the shared type's own
        // outward edge alike, while Core's and Client's references stay outside it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/shared-not-from-tool")
                    .Enforce(arch.Namespace("Shared.*").MustNotBeReferencedBy(arch.Project("Tool")))
                    .Because("The shared file answers to nothing the tool declares."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, [
                "Shared.Widget -> Shared.WidgetPart",
                "Tool.Command -> Shared.Widget"
            ]);
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_AllowEntryNamingTheCompilingProjectSatisfies()
    {
        // Strictness under attribution: the allow entry names the project that compiled both ends, which is
        // the project the edge belongs to, so the reference is allowed.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-tool")
                    .Enforce(arch.Namespace("Tool.*").MustOnlyReference(arch.Project("Tool")))
                    .Because("The tool is self-contained."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_AllowEntryNamingOnlyTheWinnerIsRed()
    {
        // The same allow-list moved to the winner. Command's reference belongs to Tool, which the entry does
        // not name, so it is unallowed — while the shared type's own outward edge stays allowed, because
        // Core declares Widget too, so that edge is intra-project in Core as much as in Tool.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-core")
                    .Enforce(arch.Project("Tool").MustOnlyReference(arch.Project("Core")))
                    .Because("The tool builds on the core alone."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_TypeofAllowEntryContainingTheTypeSatisfies()
    {
        // The other way an intra-copy edge is satisfied: an allow entry that is not a project at all simply
        // contains the type, and containment is all the strict verb asks of it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-widget")
                    .Enforce(arch.Namespace("Tool.*").MustOnlyReference(typeof(Widget)))
                    .Because("The command surface reaches widgets and nothing else."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_WinnerAnchoredContainment_DoesNotRedOnALosersOwnCopyEdge()
    {
        // The containment verb, with the subject at the target end again. Command is in neither allowed
        // project, so counting its reference into Tool's own copy would red the rule; anchored on Core the
        // subject does not count it, while User's and Consumer's references are counted and allowed.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-only-from-client")
                    .Enforce(arch.Project("Core")
                        .MustOnlyBeReferencedBy(arch.AnyOf(arch.Project("Client"), arch.Project("Core"))))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }
}

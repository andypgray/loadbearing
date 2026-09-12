using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     What a layer defined by a <see cref="Selection" /> names, and where the glob form stays its own
///     thing. A layer is transparent to its definition — the same membership, the same §4.1 attribution,
///     the same refusals — and its own adjectives narrow what the definition named. The glob form is not
///     desugared to a union, so a glob matching nothing is still a scan that found nothing rather than an
///     empty operand.
/// </summary>
public sealed class LayerDefinitionSemanticsTests
{
    [Fact]
    public void ADefinedLayer_NamesWhatItsDefinitionNames()
    {
        // Act — the layer and the bare project noun, resolved over the same codebase.
        IReadOnlyList<string> viaLayer = Checker.Selects(TwoProjectsCodebase.Model, arch => arch.Layer("Sales", arch.Project("Sales")));
        IReadOnlyList<string> viaProject = Checker.Selects(TwoProjectsCodebase.Model, arch => arch.Project("Sales"));

        // Assert — transparency: the same types in the same order, including the one no `Sales.*` glob
        // would have reached.
        viaLayer.ShouldBe(viaProject);
        viaLayer.ShouldBe(["Sales.Order", "Sales.Reports.Ledger"]);
    }

    [Fact]
    public void ADefinedLayer_AppliesItsOwnAdjectivesAfterTheDefinition()
    {
        // Act — a refinement of a layer: the head names the project, the adjective narrows it to one cone.
        IReadOnlyList<string> refined = Checker.Selects(TwoProjectsCodebase.Model, arch =>
        {
            Layer sales = arch.Layer("Sales", arch.Project("Sales"));
            return arch.Layer("Reports", sales.InNamespace("Sales.Reports.*"));
        });

        // Assert
        refined.ShouldBe(["Sales.Reports.Ledger"]);
    }

    [Fact]
    public void ADefinedLayer_AsARuleSubject_ChecksLikeItsDefinition()
    {
        // Act + Assert — the layer stands where the project noun would, and reds on the same edge.
        Checker.Run(TwoProjectsCodebase.Model, arch =>
                arch.Rule("layering/web-not-sales")
                    .Enforce(arch.Layer("Web", arch.Project("Web"))
                        .MustNotReference(arch.Layer("Sales", arch.Project("Sales"))))
                    .Because("The web layer reads sales through the reporting API."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Web.Controller", "Sales.Order");
    }

    [Fact]
    public void AProjectDefinedLayer_TakesItsDefinitionsAttributionStance()
    {
        // A type two projects declare, reached from a third: the edge Tool compiled into itself. Anchored
        // on a layer defined as project Core, the ban owns Core's declaration and this reference never
        // reaches it — the verdict the bare project noun gives (MultiplyDeclaredAttributionTests). It holds
        // only because the admission stages the project head found inside the layer; an
        // attribution-insensitive layer would forbid the intra-copy edge and red here.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-core")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Layer("Core", arch.Project("Core"))))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void AProjectDefinedLayer_StillRedsTheCrossProjectReferenceIntoItsProject()
    {
        // The other half of the same stance: Client declares no copy of the shared file, so its reference
        // crosses into Core's declaration and the layer forbids it exactly as the project noun does.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-core")
                    .Enforce(arch.Namespace("Client.*").MustNotReference(arch.Layer("Core", arch.Project("Core"))))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Client.Consumer", "Shared.Widget");
    }

    [Fact]
    public void AGlobDefinedLayer_StaysAttributionInsensitive()
    {
        // The glob form's stance is unchanged: a namespace head names a type at every project that
        // compiles it, so the same intra-copy edge the project-defined layer allowed is forbidden here.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-shared")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Layer("Shared", "Shared.*")))
                    .Because("The command surface is built on parts, not on whole widgets."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void AGlobLayer_WithOneGlobMatchingNothing_IsNotAnEmptyOperand()
    {
        // The glob form is one scan, not a union: a glob nothing matches contributes nothing and the layer
        // is what the others found. Desugaring it would put the empty glob under GRAMMAR §5.1's
        // per-operand emptiness gate and red the rule, which is the verdict change the glob form declines.
        Checker.Run(TwoProjectsCodebase.Model, arch =>
                arch.Rule("layering/sales-not-web")
                    .Enforce(arch.Layer("Sales", "Sales.*", "Invoicing.*")
                        .MustNotReference(arch.Namespace("Web.*")))
                    .Because("Sales must not reach up into the web layer."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void AUnionSubject_WithOneOperandMatchingNothing_IsTheContrast()
    {
        // The same two cones spelled as the union the glob form is not: the empty operand fails the rule in
        // its own right, which is what a desugared multi-glob layer would have started doing.
        Checker.Run(TwoProjectsCodebase.Model, arch =>
                arch.Rule("layering/sales-not-web")
                    .Enforce(arch.AnyOf(arch.Namespace("Sales.*"), arch.Namespace("Invoicing.*"))
                        .MustNotReference(arch.Namespace("Web.*")))
                    .Because("Sales must not reach up into the web layer."))
            .Single()
            .ShouldHaveFailedWithDetail(
                ViolationKind.EmptySubject,
                ConstraintEvaluator.EmptyOperandMessage("types in `Invoicing.*`"));
    }

    [Fact]
    public void AClosedGenericDefinition_IsRefusedWhereTheSameSelectionWouldBe()
    {
        // A definition is a selection like any other, so a closed generic inside one is the rule error the
        // evaluator raises anywhere else — contained per rule, named in the report, not a crash.
        Checker.Run(TwoProjectsCodebase.Model, arch =>
                arch.Rule("layering/no-lists")
                    .Enforce(arch.Layer("Lists", arch.Type(typeof(List<int>)))
                        .MustNotReference(arch.Namespace("Web.*")))
                    .Because("A closed construction has no definition-level node."))
            .Single()
            .ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "closed generic construction");
    }
}

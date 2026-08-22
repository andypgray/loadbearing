using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Caching;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     One full name, two types: a project declares a name that a referenced assembly also supplies — the
///     stand-in-under-the-package's-namespace idiom every test suite with a mock reaches for. The merge
///     carries both, and each reference resolves to whichever the referencing compilation actually bound,
///     so product code that binds the package never reads as depending on the test assembly.
/// </summary>
/// <remarks>
///     The bed is deliberately MSBuild-free and the package is a compilation handed over as a bare metadata
///     reference, never as an extraction input — if it were an input it would be a project of the solution
///     and this would be same-FQN cross-project conflation instead, which is a different rule with a
///     different note. The three surviving-edge rows are the point of the file as much as the suppressed
///     one: get the assembly comparison wrong in the other direction and every cross-project edge in the
///     suite re-points.
/// </remarks>
public sealed class PackageShadowedNameTests
{
    private const string VendorPackage = """
                                         namespace Vendor;
                                         public class Widget {}
                                         public class Gadget {}
                                         """;

    private static readonly CodebaseModel Model = Extract();

    [Fact]
    public void ExtractFromCompilations_ProductBindsThePackage_ResolvesToTheAssemblyNotTheDeclaringProject()
    {
        ReferenceEdge edge = Edge("Product.Service", "Vendor.Widget");

        edge.Target.IsExternal.ShouldBeTrue();
        edge.Target.ProjectName.ShouldBe("Vendor");
    }

    [Fact]
    public void ExtractFromCompilations_ProductBindsThePackage_ReachesNoTypeOfTheDeclaringProject()
    {
        // The acceptance criterion as an absence rather than a count: no ordering accident can satisfy it.
        // This is the phantom the field test found — a product-must-not-reference-tests rule reporting two
        // violations against a mock neither product project has a reference path to.
        Model.Edges
            .Where(edge => edge.Source.ProjectName == "Product")
            .Select(edge => edge.Target.ProjectName)
            .ShouldNotContain("Product.Tests");
    }

    [Fact]
    public void ExtractFromCompilations_TheDeclaringProjectUsesItsOwnStandIn_ReachesTheDeclaration()
    {
        // The row that stops the cure degenerating into "re-point every edge into a shadowed name". The test
        // project compiles this type itself, so it has no external record for it and nothing is foreign.
        ReferenceEdge edge = Edge("Product.Tests.WidgetTests", "Vendor.Widget");

        edge.Target.IsExternal.ShouldBeFalse();
        edge.Target.ProjectName.ShouldBe("Product.Tests");
    }

    [Fact]
    public void ExtractFromCompilations_AnUnshadowedPackageType_IsOneExternalNodeAsBefore()
    {
        ReferenceEdge edge = Edge("Product.Service", "Vendor.Gadget");

        edge.Target.IsExternal.ShouldBeTrue();
        Nodes("Vendor.Gadget")
            .Count.ShouldBe(1);
    }

    [Fact]
    public void ExtractFromCompilations_AGenuineProjectReference_KeepsItsEdge()
    {
        // The regression guard on the whole mechanism. A project reference records an external too — of the
        // referenced project's assembly — so an assembly comparison that fired here would take out every
        // cross-project edge in the suite.
        ReferenceEdge edge = Edge("Product.Service", "Core.Thing");

        edge.Target.IsExternal.ShouldBeFalse();
        edge.Target.ProjectName.ShouldBe("Product.Core");
    }

    [Fact]
    public void ExtractFromCompilations_AShadowedName_CarriesBothNodesDeclarationFirst()
    {
        Nodes("Vendor.Widget")
            .Select(node => $"{node.ProjectName} external={node.IsExternal}")
            .ShouldBe(["Product.Tests external=False", "Vendor external=True"]);
    }

    [Fact]
    public void ExtractFromCompilations_AShadowedName_IsReportedAsAMergeNote()
    {
        Model.MergeNotes.ShouldBe([
            "Project 'Product.Tests' declares 'Vendor.Widget', which referenced assembly 'Vendor' also "
            + "supplies; every reference resolves to whichever of the two the referencing compilation bound, "
            + "so a selection over project 'Product.Tests' reaches the declared one alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_NoShadowedName_SaysNothingAndMintsOneNodePerName()
    {
        CompilationInput core = CompilationFactory.Compile("Product.Core", ("Thing.cs", """
                                                                                        namespace Core;
                                                                                        public class Thing {}
                                                                                        """));
        CompilationInput consumer = CompilationFactory.CompileReferencing(
            "Product", core.Compilation, "Product.Core", ("Service.cs", """
                                                                        namespace Product;
                                                                        public class Service { private Core.Thing thing; }
                                                                        """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([consumer, core]);

        model.MergeNotes.ShouldBeEmpty();
        model.Types
            .Select(type => type.FullName)
            .ShouldBeUnique();
    }

    [Fact]
    public void Merge_FragmentsThatCannotNameTheirAssembly_MintOneNodeAndSayNothing()
    {
        // Fail-open, pinned over the same bed with the one fact blanked. A fragment that cannot name its own
        // assembly — a hand-built one, or a cache written before the fragment carried the fact — must never
        // make the merge conclude that one name denotes two types. Unreachable through CompilationFactory,
        // which always names the assembly, so the blanking is done here rather than built in.
        List<CodebaseFragment> unnamed = Inputs()
            .Select(FragmentExtractor.Extract)
            .Select(fragment => fragment with { AssemblyName = null })
            .ToList();

        CodebaseModel model = FragmentMerger.Merge(unnamed);

        model.MergeNotes.ShouldBeEmpty();
        Nodes(model, "Vendor.Widget")
            .Count.ShouldBe(1);
    }

    [Fact]
    public void Check_ProductMustNotReferenceTests_PassesOverAShadowedName()
    {
        // Acceptance criterion #1, as the rule the field test actually wrote. Green alone would prove
        // nothing — an empty subject or an empty target set is green too — so the sibling below reds on the
        // same two selections in the other direction.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("layering/product-must-not-reference-tests")
                    .Enforce(arch.Project("Product").MustNotReference(arch.Project("Product.Tests")))
                    .Because("Shipping code must not depend on test assemblies."))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void Check_TestsMustNotReferenceProduct_StillReds_SoNeitherSelectionIsEmpty()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("layering/inverted")
                    .Enforce(arch.Project("Product.Tests").MustNotReference(arch.Project("Product")))
                    .Because("The inverse of the rule above, to prove both selections are live."))
            .Single();

        result.ShouldHaveFailedWithEdge(ViolationKind.Reference, "Product.Tests.WidgetTests", "Product.Service");
    }

    [Fact]
    public void Check_ATargetNamingTheShadowedNamespace_ReachesBothNodes()
    {
        // The §4.1 consequence of two nodes, pinned where it bites: a ban that names the shadowed name must
        // cover BOTH bindings, not whichever node happened to sort last into the name index. Widget appears
        // twice below — once as the package's type, once as the stand-in — which is the whole assertion.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("layering/no-vendor")
                    .Enforce(arch.Types.MustNotReference(arch.Namespace("Vendor")))
                    .Because("Both the package's type and the stand-in wear this name."))
            .Single();

        result.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "Product.Service -> Vendor.Gadget",
                "Product.Service -> Vendor.Widget",
                "Product.Tests.WidgetTests -> Vendor.Widget"
            ]);
    }

    private static CodebaseModel Extract()
    {
        return CodebaseExtractor.ExtractFromCompilations(Inputs());
    }

    private static List<CompilationInput> Inputs()
    {
        CSharpCompilation vendor = CompilationFactory.CreateCompilation(
            "Vendor", [CompilationFactory.CoreLibrary], ("Vendor.cs", VendorPackage));

        CompilationInput core = CompilationFactory.Compile("Product.Core", ("Thing.cs", """
                                                                                        namespace Core;
                                                                                        public class Thing {}
                                                                                        """));

        // The product binds the package AND a genuine project reference, which is what lets one bed carry
        // both the shadowed row and the must-not-move row.
        var product = new CompilationInput(
            CompilationFactory.CreateCompilation(
                "Product",
                [CompilationFactory.CoreLibrary, vendor.ToMetadataReference(), core.Compilation.ToMetadataReference()],
                ("Service.cs", """
                               namespace Product;
                               public class Service
                               {
                                   private Vendor.Widget widget;
                                   private Vendor.Gadget gadget;
                                   private Core.Thing thing;
                               }
                               """)),
            "Product",
            ["Product.Core"]);

        // The test project declares the stand-in and carries NO reference to the package — which is the
        // whole reason the idiom exists — plus a real reference to the product, the control edge.
        var tests = new CompilationInput(
            CompilationFactory.CreateCompilation(
                "Product.Tests",
                [CompilationFactory.CoreLibrary, product.Compilation.ToMetadataReference()],
                ("Mocks/Vendor.cs", """
                                    namespace Vendor;
                                    public class Widget {}
                                    """),
                ("WidgetTests.cs", """
                                   namespace Product.Tests;
                                   public class WidgetTests
                                   {
                                       private Vendor.Widget stub;
                                       private Product.Service subject;
                                   }
                                   """)),
            "Product.Tests",
            ["Product"]);

        return [product, core, tests];
    }

    private static ReferenceEdge Edge(string source, string target)
    {
        return Model.Edges.Single(edge => edge.Source.FullName == source && edge.Target.FullName == target);
    }

    private static IReadOnlyList<TypeNode> Nodes(string fullName)
    {
        return Nodes(Model, fullName);
    }

    // Deliberately not ModelQuery.Type: its Single is exactly the uniqueness assumption this file exists to
    // disprove, and a shadowed name would red it with an unhelpful message.
    private static IReadOnlyList<TypeNode> Nodes(CodebaseModel model, string fullName)
    {
        return model.Types
            .Where(type => type.FullName == fullName)
            .ToList();
    }
}

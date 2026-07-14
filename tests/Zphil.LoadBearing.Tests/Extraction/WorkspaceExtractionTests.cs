using Microsoft.Build.Locator;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>
///     The MSBuildWorkspace tier: the fixture solution is loaded and extracted once by
///     <see cref="WorkspaceFixture" />; these tests read the shared model. Pinned strings are the
///     spec — moving one is a deliberate act.
/// </summary>
public sealed class WorkspaceExtractionTests(WorkspaceFixture fixture)
{
    [Fact]
    public void LoadAsync_FixtureSolution_LoadsThreeProjectsWithLocatorRegistered()
    {
        MSBuildLocator.IsRegistered.ShouldBeTrue();
        fixture.Model.Projects.Select(p => p.Name)
            .ShouldBe(["MyApp.Domain", "MyApp.Legacy.Billing", "MyApp.Web"]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_FixtureSolution_PinsProjectReferenceGraph()
    {
        ProjectNode Project(string name)
        {
            return fixture.Model.Projects.Single(p => p.Name == name);
        }

        Project("MyApp.Domain").ProjectReferences.ShouldBe(["MyApp.Web"]);
        Project("MyApp.Web").ProjectReferences.ShouldBe(["MyApp.Legacy.Billing"]);
        Project("MyApp.Legacy.Billing").ProjectReferences.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromSolutionAsync_DomainProject_PinsCompleteEdgeList()
    {
        string rendered = string.Join("\n", fixture.Model.Edges
            .Where(e => e.Source.ProjectName == "MyApp.Domain")
            .Select(fixture.RenderEdge));

        rendered.ShouldBe(
            """
            MyApp.Domain.Order -> MyApp.Domain.Money @ MyApp.Domain/Order.Validation.cs:5, MyApp.Domain/Order.cs:7, MyApp.Domain/Order.cs:9
            MyApp.Domain.Order -> MyApp.Domain.Order.Line @ MyApp.Domain/Order.cs:9
            MyApp.Domain.Order.Line -> MyApp.Domain.Money @ MyApp.Domain/Order.cs:13, MyApp.Domain/Order.cs:21
            MyApp.Domain.OrderService -> MyApp.Domain.Order @ MyApp.Domain/OrderService.cs:7, MyApp.Domain/OrderService.cs:10
            MyApp.Domain.OrderService -> MyApp.Web.HomeController @ MyApp.Domain/OrderService.cs:9, MyApp.Domain/OrderService.cs:10, MyApp.Domain/OrderService.cs:13
            MyApp.Domain.OrderService -> MyApp.Web.WebTextExtensions @ MyApp.Domain/OrderService.cs:10
            MyApp.Domain.PricingStrategy -> MyApp.Domain.Money @ MyApp.Domain/PricingStrategy.cs:3
            MyApp.Domain.PricingStrategy -> MyApp.Domain.Order @ MyApp.Domain/PricingStrategy.cs:3
            """);
    }

    [Fact]
    public void ExtractFromSolutionAsync_WebToBillingFacadeEdge_PinsCleanFacadeSites()
    {
        fixture.Model.Edge("MyApp.Web.HomeController", "MyApp.Legacy.Billing.IBillingFacade")
            .Lines().ShouldBe([17, 20]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_WebToBillingCalculatorEdge_PinsNonFacadeSites()
    {
        fixture.Model.Edge("MyApp.Web.InvoiceController", "MyApp.Legacy.Billing.BillingCalculator")
            .Lines().ShouldBe([9, 10]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_InvoiceControllerToDataTable_PinsGrandfatheredMigrateEdge()
    {
        // The site the Migrate baseline grandfathers: InvoiceController's inline DataTable (Phase 5).
        ReferenceEdge edge = fixture.Model.Edge("MyApp.Web.InvoiceController", "System.Data.DataTable");

        edge.Target.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([14, 16]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_HomeControllerToDataTable_PinsNewRedMigrateEdge()
    {
        // New code in the OLD pattern — HomeController's DataTable is not in the baseline, so it stays red.
        ReferenceEdge edge = fixture.Model.Edge("MyApp.Web.HomeController", "System.Data.DataTable");

        edge.Target.IsExternal.ShouldBeTrue();
        edge.Lines().ShouldBe([24, 26]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_SymbolIds_PinRealWorkspaceDocIdForms()
    {
        // The baseline keys (GRAMMAR §4.3) from the real workspace: plain, open generic (backtick-arity),
        // nested (dotted), and external.
        fixture.Model.Type("MyApp.Web.HomeController").SymbolId.ShouldBe("T:MyApp.Web.HomeController");
        fixture.Model.Type("MyApp.Web.IHandler<T>").SymbolId.ShouldBe("T:MyApp.Web.IHandler`1");
        fixture.Model.Type("MyApp.Domain.Order.Line").SymbolId.ShouldBe("T:MyApp.Domain.Order.Line");
        fixture.Model.Type("System.Data.DataTable").SymbolId.ShouldBe("T:System.Data.DataTable");
    }

    [Fact]
    public void ExtractFromSolutionAsync_StringBuilderTarget_IsExternalWithPinnedSites()
    {
        ReferenceEdge edge = fixture.Model.Edge("MyApp.Web.HomeController", "System.Text.StringBuilder");

        edge.Target.IsExternal.ShouldBeTrue();
        edge.Target.ProjectName.ShouldNotBeNullOrEmpty();
        edge.Lines().ShouldBe([9, 13, 14, 19]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_PartialOrder_IsOneNodeWithTwoDeclarationSites()
    {
        fixture.Model.Type("MyApp.Domain.Order").DeclarationSites
            .Select(s => $"{fixture.RelativePath(s)}:{s.Line}")
            .ShouldBe(["MyApp.Domain/Order.Validation.cs:3", "MyApp.Domain/Order.cs:3"]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_FixtureKinds_MapToCoreTypeKinds()
    {
        fixture.Model.Type("MyApp.Domain.Money").Kind.ShouldBe(TypeKind.Struct);
        fixture.Model.Type("MyApp.Web.InvoiceCreated").Kind.ShouldBe(TypeKind.Class);
        fixture.Model.Type("MyApp.Domain.PricingStrategy").Kind.ShouldBe(TypeKind.Delegate);
        fixture.Model.Type("MyApp.Legacy.Billing.RoundingMode").Kind.ShouldBe(TypeKind.Enum);
        fixture.Model.Type("MyApp.Web.IHandler<T>").Kind.ShouldBe(TypeKind.Interface);
        fixture.Model.Type("MyApp.Web.WebTextExtensions").Kind.ShouldBe(TypeKind.Class);
    }

    [Fact]
    public void ExtractFromSolutionAsync_GenericAndNestedFullNames_RenderPinnedForms()
    {
        fixture.Model.Types.ShouldContain(t => t.FullName == "MyApp.Web.IHandler<T>");
        fixture.Model.Types.ShouldContain(t => t.FullName == "MyApp.Domain.Order.Line");
    }

    [Fact]
    public void ExtractFromSolutionAsync_ShapeFacts_SurviveTheRealWorkspace()
    {
        TypeNode ext = fixture.Model.Type("MyApp.Web.WebTextExtensions");
        ext.IsStatic.ShouldBeTrue();
        ext.IsSealed.ShouldBeFalse();
        ext.IsAbstract.ShouldBeFalse();
        ext.Accessibility.ShouldBe(Accessibility.Public);

        // The pinned two-file partial: FilePaths verbatim (real workspace paths are absolute with
        // mixed separators, hence normalize-then-EndsWith) in site order — Order.Validation.cs sorts
        // before Order.cs ('V' < 'c' ordinal).
        TypeNode order = fixture.Model.Type("MyApp.Domain.Order");
        order.FilePaths.Count.ShouldBe(2);
        order.FilePaths[0].Replace('\\', '/').ShouldEndWith("MyApp.Domain/Order.Validation.cs");
        order.FilePaths[1].Replace('\\', '/').ShouldEndWith("MyApp.Domain/Order.cs");

        // Already-pinned external: real metadata scalars, empty sites.
        TypeNode sb = fixture.Model.Type("System.Text.StringBuilder");
        sb.IsSealed.ShouldBeTrue();
        sb.FilePaths.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromSolutionAsync_HomeController_PopulatesITypeInfoSurface()
    {
        TypeNode home = fixture.Model.Type("MyApp.Web.HomeController");
        home.Attributes.Select(a => a.FullName()).ShouldBe(["MyApp.Web.WebRouteAttribute"]);
        var homeBase = (TypeNode)home.BaseType!;
        homeBase.FullName.ShouldBe("System.Object");
        homeBase.IsExternal.ShouldBeTrue();
        home.DeclarationSites.Single().Line.ShouldBe(7);

        ((TypeNode)fixture.Model.Type("MyApp.Web.WebRouteAttribute").BaseType!).FullName.ShouldBe("System.Attribute");

        fixture.Model.Type("MyApp.Web.InvoiceCreatedHandler").Interfaces.Select(i => i.FullName())
            .ShouldBe(["MyApp.Web.IHandler<T>"]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_HandlerConstructions_PinConstructedInterfaceName()
    {
        TypeConstruction handler = fixture.Model.Type("MyApp.Web.InvoiceCreatedHandler").AllInterfaces
            .Single(c => c.Definition.FullName == "MyApp.Web.IHandler<T>");
        handler.FullName.ShouldBe("MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>");

        fixture.Model.Type("MyApp.Web.RefundProcessor").AllInterfaces
            .Single().FullName.ShouldBe("MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>");
    }

    [Fact]
    public void ExtractFromSolutionAsync_HomeControllerConstructions_PinAttributeAndBaseChain()
    {
        TypeNode home = fixture.Model.Type("MyApp.Web.HomeController");
        home.AttributeConstructions.Select(c => c.FullName).ShouldBe(["MyApp.Web.WebRouteAttribute"]);
        home.BaseTypeChain.Select(c => c.FullName).ShouldBe(["System.Object"]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_ExternalObjectNode_HasShallowConstructionLists()
    {
        TypeNode obj = fixture.Model.Type("System.Object");
        obj.IsExternal.ShouldBeTrue();
        obj.AllInterfaces.ShouldBeEmpty();
        obj.BaseTypeChain.ShouldBeEmpty();
        obj.AttributeConstructions.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_BillingSources_MatchWorkspaceExtractionExactly()
    {
        string billingDir = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MyApp", "MyApp.Legacy.Billing");
        (string Path, string Source)[] files = Directory.EnumerateFiles(billingDir, "*.cs")
            .Select(f => ($"MyApp.Legacy.Billing/{Path.GetFileName(f)}", File.ReadAllText(f)))
            .ToArray();

        CodebaseModel fast = CompilationFactory.Extract("MyApp.Legacy.Billing", files);

        var fastEdges = RenderBillingEdges(fast.Edges, s => s.FilePath);
        var workspaceEdges = RenderBillingEdges(fixture.Model.Edges, fixture.RelativePath);

        workspaceEdges.ShouldBe(fastEdges);
        fastEdges.ShouldNotBeEmpty();
    }

    private static List<string> RenderBillingEdges(IEnumerable<ReferenceEdge> edges, Func<SourceLocation, string> path)
    {
        return edges
            .Where(e => e.Source.ProjectName == "MyApp.Legacy.Billing")
            .Select(e => $"{e.Source.FullName} -> {e.Target.FullName} (external={e.Target.IsExternal}) @ "
                         + string.Join(", ", e.Sites.Select(s => $"{path(s)}:{s.Line}")))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }
}
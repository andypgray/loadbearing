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
    public void ExtractFromSolutionAsync_DomainProject_PinsCompleteMemberEdgeList()
    {
        // The member-use channel (GRAMMAR §4.5) for the Domain project, pinned whole: property accesses fold
        // to P: (Reference, Amount), the cross-project call is the M: overload (RenderOrder), and the reduced
        // extension resolves to the declaring static class (WebTextExtensions), never to string. Deliberately
        // ABSENT: OrderService's `new HomeController()` (constructor) and `typeof(HomeController)` — neither is a use.
        string rendered = string.Join("\n", fixture.Model.MemberEdges
            .Where(e => e.Source.ProjectName == "MyApp.Domain")
            .Select(fixture.RenderMemberEdge));

        rendered.ShouldBe(
            """
            MyApp.Domain.Order -> P:MyApp.Domain.Money.Amount @ MyApp.Domain/Order.Validation.cs:5
            MyApp.Domain.OrderService -> M:MyApp.Web.HomeController.RenderOrder(System.String) @ MyApp.Domain/OrderService.cs:10
            MyApp.Domain.OrderService -> M:MyApp.Web.WebTextExtensions.ToWebDisplay(System.String) @ MyApp.Domain/OrderService.cs:10
            MyApp.Domain.OrderService -> P:MyApp.Domain.Order.Reference @ MyApp.Domain/OrderService.cs:10
            """);
    }

    [Fact]
    public void ExtractFromSolutionAsync_DomainProject_PinsCompleteConstructorEdgeList()
    {
        // The construction channel (GRAMMAR §4.5) for the Domain project, pinned whole: Order builds its nested
        // Line, and OrderService `new`s the cross-project HomeController. Deliberately ABSENT: OrderService's
        // typeof(HomeController) at line 13 (a type reference, not a construction — no ctor edge).
        string rendered = string.Join("\n", fixture.Model.ConstructorEdges
            .Where(e => e.Source.ProjectName == "MyApp.Domain")
            .Select(fixture.RenderConstructorEdge));

        rendered.ShouldBe(
            """
            MyApp.Domain.Order -> MyApp.Domain.Order.Line @ MyApp.Domain/Order.cs:9
            MyApp.Domain.OrderService -> MyApp.Web.HomeController @ MyApp.Domain/OrderService.cs:9
            """);

        // Cross-project construction resolves to the declared Web node, and the type edge co-exists.
        fixture.Model.ConstructorEdge("MyApp.Domain.OrderService", "MyApp.Web.HomeController")
            .Constructed.IsExternal.ShouldBeFalse();
        fixture.Model.HasEdge("MyApp.Domain.OrderService", "MyApp.Web.HomeController").ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromSolutionAsync_BillingCalculatorMemberEdges_PinExternalNormalizedForms()
    {
        // The real-workspace member DocIds against BCL metadata: the two Math.Round overloads are distinct
        // M: ids (the §4.3 per-overload identity substrate), the enum members are F:, and the external flag is
        // set for the BCL targets but not the in-solution RoundingMode.
        string rendered = string.Join("\n", fixture.Model.MemberEdges
            .Where(e => e.Source.FullName == "MyApp.Legacy.Billing.BillingCalculator")
            .Select(fixture.RenderMemberEdge));

        rendered.ShouldBe(
            """
            MyApp.Legacy.Billing.BillingCalculator -> F:MyApp.Legacy.Billing.RoundingMode.Bankers @ MyApp.Legacy.Billing/BillingCalculator.cs:9
            MyApp.Legacy.Billing.BillingCalculator -> F:System.MidpointRounding.ToEven @ MyApp.Legacy.Billing/BillingCalculator.cs:10
            MyApp.Legacy.Billing.BillingCalculator -> M:System.Math.Round(System.Decimal,System.Int32) @ MyApp.Legacy.Billing/BillingCalculator.cs:11
            MyApp.Legacy.Billing.BillingCalculator -> M:System.Math.Round(System.Decimal,System.Int32,System.MidpointRounding) @ MyApp.Legacy.Billing/BillingCalculator.cs:10
            """);

        MemberReference round = fixture.Model.MemberEdge(
            "MyApp.Legacy.Billing.BillingCalculator", "M:System.Math.Round(System.Decimal,System.Int32)").Member;
        round.ContainingType.IsExternal.ShouldBeTrue();
        round.ContainingType.ShouldBeSameAs(fixture.Model.Type("System.Math"));

        fixture.Model.MemberEdge(
                "MyApp.Legacy.Billing.BillingCalculator", "F:MyApp.Legacy.Billing.RoundingMode.Bankers")
            .Member.ContainingType.IsExternal.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromSolutionAsync_HomeControllerStringBuilderUse_UnionsSitesUnderOneExternalMethodEdge()
    {
        // Two Append call sites (lines 13, 19) union into one external M: edge; the constructor at line 9 is not here.
        MemberEdge append = fixture.Model.MemberEdge(
            "MyApp.Web.HomeController", "M:System.Text.StringBuilder.Append(System.String)");

        append.Member.ContainingType.IsExternal.ShouldBeTrue();
        append.Lines().ShouldBe([13, 19]);
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
        // The site the Migrate baseline grandfathers: InvoiceController's inline DataTable.
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
    public void ExtractFromSolutionAsync_HomeControllerClockReads_PinNewMemberUseEdgesAndDateTimeTypeEdge()
    {
        // The member-use fixture edit (GRAMMAR §4.5): HomeController's two ambient-clock reads fold to
        // P: member edges on the new external System.DateTime at the appended lines — the rows time/inject-clock
        // bans. The parallel type edge to System.DateTime (return types + the reads) is the new external node.
        fixture.Model.MemberEdge("MyApp.Web.HomeController", "P:System.DateTime.Now").Lines().ShouldBe([32]);
        fixture.Model.MemberEdge("MyApp.Web.HomeController", "P:System.DateTime.UtcNow").Lines().ShouldBe([37]);

        ReferenceEdge dateTime = fixture.Model.Edge("MyApp.Web.HomeController", "System.DateTime");
        dateTime.Target.IsExternal.ShouldBeTrue();
        dateTime.Lines().ShouldBe([30, 32, 35, 37]);
    }

    [Fact]
    public void ExtractFromSolutionAsync_HomeControllerMembers_PinDeclaredInventoryAndFacts()
    {
        // The member inventory (GRAMMAR §4.6) on the real workspace: the nine declared members (the implicit
        // default constructor excluded), ordered ordinal by SymbolId. Return/member types are definition-level
        // FQNs, accessibility carries through (the private field included), and each member's DeclaringType is
        // the SAME node instance held by model.Types. The three Task-returning methods are the
        // naming/async-suffix subject universe — Load returns Task<int> (a definition-level Task`1).
        TypeNode home = fixture.Model.Type("MyApp.Web.HomeController");

        home.MemberIds().ShouldBe([
            "F:MyApp.Web.HomeController.log",
            "M:MyApp.Web.HomeController.ExportOrders",
            "M:MyApp.Web.HomeController.ExportStamp",
            "M:MyApp.Web.HomeController.ExportStampUtc",
            "M:MyApp.Web.HomeController.Load",
            "M:MyApp.Web.HomeController.RenderOrder(System.String)",
            "M:MyApp.Web.HomeController.Save",
            "M:MyApp.Web.HomeController.SaveAsync",
            "M:MyApp.Web.HomeController.ShowInvoiceTotal(MyApp.Legacy.Billing.IBillingFacade)"
        ]);

        MemberNode log = home.Member("F:MyApp.Web.HomeController.log");
        log.Kind.ShouldBe(MemberKind.Field);
        log.Accessibility.ShouldBe(Accessibility.Private);
        log.MemberTypeFullName.ShouldBe("System.Text.StringBuilder");
        log.DeclaringType.ShouldBeSameAs(home);

        MemberNode stamp = home.Member("M:MyApp.Web.HomeController.ExportStamp");
        stamp.Kind.ShouldBe(MemberKind.Method);
        stamp.Accessibility.ShouldBe(Accessibility.Public);
        stamp.ReturnTypeFullName.ShouldBe("System.DateTime");
        stamp.DeclarationSites.Single().Line.ShouldBe(30);

        home.Member("M:MyApp.Web.HomeController.RenderOrder(System.String)").ReturnTypeFullName.ShouldBe("System.String");

        // Externals carry no inventory (the member axis is solution-declared-only).
        fixture.Model.Type("System.Text.StringBuilder").Members.ShouldBeEmpty();
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

    [Fact]
    public void ExtractFromSolutionAsync_ReportSchedulerInjection_PinsCaptiveConstructorEdges()
    {
        // The injection channel (GRAMMAR §4.7) for the captive-dependency flagship: the singleton
        // ReportScheduler's primary constructor takes a scoped IOrderFeed and a transient IOrderFormatter,
        // each at its own parameter file:line — the two edges di/no-captive-dependencies reds on.
        string rendered = string.Join("\n", fixture.Model.InjectionEdges
            .Where(e => e.Source.FullName == "MyApp.Web.ReportScheduler")
            .Select(fixture.RenderInjectionEdge));

        rendered.ShouldBe(
            """
            MyApp.Web.ReportScheduler -> MyApp.Web.IOrderFeed @ MyApp.Web/ReportScheduler.cs:7
            MyApp.Web.ReportScheduler -> MyApp.Web.IOrderFormatter @ MyApp.Web/ReportScheduler.cs:8
            """);

        // Both injected endpoints are in-solution nodes (reference equality, not just name), so the
        // Registered(Scoped)/Registered(Transient) operands match them as members.
        fixture.Model.InjectionEdge("MyApp.Web.ReportScheduler", "MyApp.Web.IOrderFeed")
            .Injected.IsExternal.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromSolutionAsync_ServiceWiring_PinsThreeRegistrationFacts()
    {
        // The registration channel (GRAMMAR §4.7): ServiceWiring.Configure's three recognized calls, each
        // keyed (lifetime, service, implementation) at the invoked-method file:line. AddSingleton<ReportScheduler>()
        // is the one-type-arg self form (service == implementation); the other two name a distinct
        // implementation. The whole list is pinned — these are the only registrations the fixture spells.
        string rendered = string.Join("\n", fixture.Model.ServiceRegistrations.Select(fixture.RenderRegistration));

        rendered.ShouldBe(
            """
            Singleton MyApp.Web.ReportScheduler -> MyApp.Web.ReportScheduler @ MyApp.Web/ServiceWiring.cs:13
            Scoped MyApp.Web.IOrderFeed -> MyApp.Web.OrderFeed @ MyApp.Web/ServiceWiring.cs:14
            Transient MyApp.Web.IOrderFormatter -> MyApp.Web.OrderFormatter @ MyApp.Web/ServiceWiring.cs:15
            """);
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